using CopyTranslated.Windows;
using Dalamud;
using Dalamud.ContextMenu;
using Dalamud.Data;
using Dalamud.Game.ClientState;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;
using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CopyTranslated
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Item Translator Plugin";

        private readonly DalamudPluginInterface pluginInterface;
        private readonly CommandManager commandManager;

        private readonly ChatGui chatGui;
        private readonly GameGui gameGui;
        private readonly ClientState clientState;
        private readonly DataManager dataManager;

        private readonly DalamudContextMenu contextMenu;
        private readonly GameObjectContextMenuItem gameObjectContextMenuItem;
        private readonly InventoryContextMenuItem inventoryContextMenuItem;

        public Configuration Configuration { get; }
        public WindowSystem WindowSystem { get; } = new("ItemTranslatorPlugin");
        private readonly ConfigWindow configWindow;

        private bool? isSheetAvailableCache;
        private bool isTraditionalChinese = false;
        private ExcelSheet<Item>? itemSheetCache;
        private readonly Dictionary<string, string> languageFilterCache = new();

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            [RequiredVersion("1.0")] ChatGui chatGui,
            [RequiredVersion("1.0")] GameGui gameGui,
            [RequiredVersion("1.0")] ClientState clientState,
            [RequiredVersion("1.0")] DataManager dataManager)
        {
            this.pluginInterface = pluginInterface;
            this.commandManager = commandManager;
            this.chatGui = chatGui;
            this.gameGui = gameGui;
            this.clientState = clientState;
            this.dataManager = dataManager;

            Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(pluginInterface);

            configWindow = new ConfigWindow(this);
            WindowSystem.AddWindow(configWindow);

            pluginInterface.UiBuilder.Draw += DrawUI;
            pluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

            contextMenu = new DalamudContextMenu();
            gameObjectContextMenuItem = new GameObjectContextMenuItem(
                new SeString(new TextPayload("Copy Translated")), Lookup, true);
            contextMenu.OnOpenGameObjectContextMenu += OpenGameObjectContextMenu;

            inventoryContextMenuItem = new InventoryContextMenuItem(
                new SeString(new TextPayload("Copy Translated")), InventoryLookup, true);
            contextMenu.OnOpenInventoryContextMenu += OpenInventoryContextMenu;

            Initialize();
            configWindow.OnLanguageChanged += Initialize;
        }

        public void Dispose()
        {
            configWindow.OnLanguageChanged -= Initialize;

            contextMenu.OnOpenGameObjectContextMenu -= OpenGameObjectContextMenu;
            contextMenu.OnOpenInventoryContextMenu -= OpenInventoryContextMenu;
            contextMenu.Dispose();

            WindowSystem.RemoveAllWindows();
            configWindow.Dispose();

            isSheetAvailableCache = null;
            itemSheetCache = null;
            languageFilterCache.Clear();

            if (LazyHttpClient.IsValueCreated) LazyHttpClient.Value.Dispose();
        }

        private void DrawUI() => WindowSystem.Draw();

        public void DrawConfigUI() => configWindow.IsOpen = true;

        private void Initialize()
        {
            isTraditionalChinese = false;

            try
            {
                itemSheetCache = dataManager.GetExcelSheet<Item>(MapLanguageToClientLanguage(Configuration.SelectedLanguage));
            }
            catch (Exception ex)
            {
                OutputChatLine($"Error: Failed to fetch Excel sheet for language {Configuration.SelectedLanguage}. " +
                    $"Details: {ex.Message}");
            }
            isSheetAvailableCache = IsSheetAvailable(itemSheetCache);

            languageFilterCache.Clear();
        }

        internal void OutputChatLine(SeString message)
        {
            SeStringBuilder sb = new();
            sb.AddUiForeground("[Item Translated] ", 45).Append(message);

            chatGui.PrintChat(new XivChatEntry { Message = sb.BuiltString });
        }

        private void OpenGameObjectContextMenu(GameObjectContextMenuOpenArgs args)
        {
            if (args.ObjectWorld != 0) return;
            args.AddCustomItem(gameObjectContextMenuItem);
        }

        private void OpenInventoryContextMenu(InventoryContextMenuOpenArgs args)
        {
            args.AddCustomItem(inventoryContextMenuItem);
        }

        private unsafe void Lookup(GameObjectContextMenuItemSelectedArgs args)
        {
            uint itemId;

            switch (args.ParentAddonName)
            {
                case "ContentsInfoDetail":
                    {
                        UIModule* uiModule = (UIModule*)gameGui.GetUIModule();
                        AgentModule* agents = uiModule->GetAgentModule();
                        AgentInterface* agent = agents->GetAgentByInternalId(AgentId.ContentsTimer);
                        itemId = *(uint*)((nint)agent + 0x17CC);
                        break;
                    }
                case "RecipeNote":
                    itemId = *(uint*)(gameGui.FindAgentInterface(args.ParentAddonName) + 0x398);
                    break;
                case "ChatLog":
                    itemId = *(uint*)(gameGui.FindAgentInterface(args.ParentAddonName) + 0x948);
                    break;
                case "DailyQuestSupply":
                    itemId = *(uint*)(gameGui.FindAgentInterface(args.ParentAddonName) + 0x54);
                    break;
                case "RecipeTree":
                case "RecipeMaterialList":
                    {
                        UIModule* uiModule = (UIModule*)gameGui.GetUIModule();
                        AgentModule* agents = uiModule->GetAgentModule();
                        AgentInterface* agent = agents->GetAgentByInternalId(AgentId.RecipeItemContext);
                        itemId = *(uint*)((nint)agent + 0x28);
                        break;
                    }
                default:
                    itemId = (uint)gameGui.HoveredItem;
                    if (itemId == 0) OutputChatLine($"Error: {itemId},{args.ParentAddonName}\nReport to developer.");
                    break;
            }
            GetItemInfoAndCopyToClipboard(itemId, Configuration.SelectedLanguage);
        }

        private void InventoryLookup(InventoryContextMenuItemSelectedArgs args)
        {
            GetItemInfoAndCopyToClipboard(args.ItemId, Configuration.SelectedLanguage);
        }

        private bool IsSheetAvailable(ExcelSheet<Item>? sheet)
        {
            if (clientState.ClientLanguage != MapLanguageToClientLanguage(Configuration.SelectedLanguage)) return true;

            string testItemName;

            switch (clientState.ClientLanguage)
            {
                case ClientLanguage.English:
                    testItemName = "Cobalt Ingot";
                    break;
                case ClientLanguage.Japanese:
                    testItemName = "コバルトインゴット";
                    break;
                case ClientLanguage.German:
                    testItemName = "Koboldeisenbarren";
                    break;
                case ClientLanguage.French:
                    testItemName = "Lingot de cobalt";
                    break;
                default:
                    testItemName = "Cobalt Ingot";
                    break;
            }

            var retrievedItemName = sheet?.GetRow(5059)?.Name ?? "";

            if (string.IsNullOrEmpty(retrievedItemName)) return false;
            return retrievedItemName == testItemName;
        }

        public void GetItemInfoAndCopyToClipboard(uint itemId, string language)
        {
            if (itemId == 0) return;
            if (isSheetAvailableCache == false)
            {
                Task.Run(() => GetItemNameByApi(itemId, language));
                return;
            }

            Item? item = null;
            item = itemSheetCache?.GetRow(itemId);

            var itemName = item?.Name ?? string.Empty;
            if (string.IsNullOrEmpty(itemName))
            {
                OutputChatLine($"Error: Item not found in {language} database for ID {itemId}");
                return;
            }
            else
            {
                ImGui.SetClipboardText(itemName);
                OutputChatLine($"Item copied: {itemName}");
            }
        }

        private static ClientLanguage MapLanguageToClientLanguage(string fullLanguageName) => fullLanguageName switch
        {
            "English" => ClientLanguage.English,
            "Japanese" => ClientLanguage.Japanese,
            "German" => ClientLanguage.German,
            "French" => ClientLanguage.French,
            _ => ClientLanguage.English
        };

        private string MapLanguageToFilter(string fullLanguageName)
        {
            if (languageFilterCache.TryGetValue(fullLanguageName, out var Filter))
            {
                return Filter;
            }

            if (fullLanguageName == "Chinese (Traditional)")
            {
                Filter = "Name_chs";
                isTraditionalChinese = true;
                languageFilterCache[fullLanguageName] = Filter;
                return Filter;
            }

            Filter = fullLanguageName switch
            {
                "English" => "Name_en",
                "Japanese" => "Name_ja",
                "German" => "Name_de",
                "French" => "Name_fr",
                "Chinese (Simplified)" => "Name_chs",
                _ => "Name_en"
            };

            languageFilterCache[fullLanguageName] = Filter;
            return Filter;
        }

        private static Lazy<HttpClient> LazyHttpClient = new Lazy<HttpClient>(() => new HttpClient());

        private async Task GetItemNameByApi(uint itemId, string language)
        {
            if (itemId == 0) return;

            var filter = MapLanguageToFilter(language);
            string apiUrl = filter == "Name_chs" ?
                $"https://cafemaker.wakingsands.com/Item/{itemId}?columns={filter}" :
                $"https://xivapi.com/Item/{itemId}?columns={filter}";

            try
            {
                var jsonContent = await LazyHttpClient.Value.GetStringAsync(apiUrl);

                var match = Regex.Match(jsonContent, @":\""(.*?)\""}");

                string itemName = match.Groups[1].Value;

                if (filter == "Name_chs")
                {
                    itemName = Regex.Unescape(itemName);
                    if (isTraditionalChinese) 
                    {
                        itemName = ChineseConverter.Convert(itemName, ChineseConversionDirection.SimplifiedToTraditional);
                    } ; 
                }
                if (string.IsNullOrEmpty(itemName))
                {
                    OutputChatLine($"Error: API error at {DateTime.Now:HH:mm:ss tt} {apiUrl} returned {jsonContent}");
                }

                else
                {
                    ImGui.SetClipboardText(itemName);
                    OutputChatLine($"Item copied: {itemName}");
                }
            }
            catch (Exception ex)
            {
                OutputChatLine($"Error: {ex.Message}");
            }
        }
    }
}

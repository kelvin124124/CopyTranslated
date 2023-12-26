using CopyTranslated.Windows;
using Dalamud;
using Dalamud.ContextMenu;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI;
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
        public static string Name => "CopyTranslated";
        private const string CommandName = "/pcopy";

        private readonly DalamudPluginInterface pluginInterface;
        private readonly ICommandManager commandManager;

        private readonly IChatGui chatGui;
        private readonly IGameGui gameGui;
        private readonly IClientState clientState;
        private readonly IDataManager dataManager;

        public readonly DalamudContextMenu contextMenu;
        public readonly GameObjectContextMenuItem gameObjectContextMenuItem;
        public readonly InventoryContextMenuItem inventoryContextMenuItem;

        public Configuration Configuration { get; }
        public WindowSystem WindowSystem { get; } = new("CopyTranslated");
        private readonly ConfigWindow configWindow;

        private bool? isSheetAvailableCache;
        private bool isTraditionalChinese = false;
        private ExcelSheet<Item>? itemSheetCache;
        private readonly Dictionary<string, string> languageFilterCache = [];

        private uint hoveredItemId = 0;

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] ICommandManager commandManager,
            [RequiredVersion("1.0")] IChatGui chatGui,
            [RequiredVersion("1.0")] IGameGui gameGui,
            [RequiredVersion("1.0")] IClientState clientState,
            [RequiredVersion("1.0")] IDataManager dataManager)
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

            contextMenu = new DalamudContextMenu(pluginInterface);
            gameObjectContextMenuItem = new GameObjectContextMenuItem(
                new SeString(new TextPayload("Copy Translated")), Lookup, true);
            contextMenu.OnOpenGameObjectContextMenu += OpenGameObjectContextMenu;

            inventoryContextMenuItem = new InventoryContextMenuItem(
                new SeString(new TextPayload("Copy Translated")), InventoryLookup, true);
            contextMenu.OnOpenInventoryContextMenu += OpenInventoryContextMenu;

            commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open copy translated configuration window."
            });

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
        private void OnCommand(string command, string args)
        {
            configWindow.IsOpen = true;
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
            sb.AddUiForeground("[CT] ", 58).Append(message);

            chatGui.Print(new XivChatEntry { Message = sb.BuiltString });
        }

        private unsafe void OpenGameObjectContextMenu(GameObjectContextMenuOpenArgs args)
        {
            // prevent showing the option when player is selected
            if (args.ObjectWorld != 0) return;

            hoveredItemId = (uint)gameGui.HoveredItem;

            if (args.ParentAddonName != "RecipeNote"
                && args.ParentAddonName != "RecipeTree"
                && args.ParentAddonName != "RecipeMaterialList"
                && args.ParentAddonName != "ContentsInfoDetail") 
            {
                // prevent showing the option when action is selected
                if (hoveredItemId == 0) return;
            }

            args.AddCustomItem(gameObjectContextMenuItem);
        }

        private void OpenInventoryContextMenu(InventoryContextMenuOpenArgs args)
        {
            args.AddCustomItem(inventoryContextMenuItem);
        }

        private unsafe void Lookup(GameObjectContextMenuItemSelectedArgs args)
        {
            uint itemId = hoveredItemId % 500000;

            if (args.ParentAddonName == "ContentsInfoDetail")
            {
                UIModule* uiModule = (UIModule*)gameGui.GetUIModule();
                AgentModule* agents = uiModule->GetAgentModule();
                AgentInterface* agent = agents->GetAgentByInternalId(AgentId.ContentsTimer);

                itemId = *(uint*)((nint)agent + 0x17CC);
            }
            else if (args.ParentAddonName == "RecipeNote")
            {
                itemId = *(uint*)(gameGui.FindAgentInterface(args.ParentAddonName) + 0x398);
            }
            else if (args.ParentAddonName == "RecipeTree" || args.ParentAddonName == "RecipeMaterialList")
            {
                UIModule* uiModule = (UIModule*)gameGui.GetUIModule();
                AgentModule* agents = uiModule->GetAgentModule();
                AgentInterface* agent = agents->GetAgentByInternalId(AgentId.RecipeItemContext);

                itemId = *(uint*)((nint)agent + 0x28);
            }

            GetItemInfoAndCopyToClipboard(itemId, Configuration.SelectedLanguage);
        }

        private void InventoryLookup(InventoryContextMenuItemSelectedArgs args)
        {
            GetItemInfoAndCopyToClipboard(args.ItemId, Configuration.SelectedLanguage);
        }

        // Detect language packs
        private bool IsSheetAvailable(ExcelSheet<Item>? sheet)
        {
            if (Configuration.SelectedLanguage == "Chinese (Simplified)" || Configuration.SelectedLanguage == "Chinese (Traditional)") return false;
            if (clientState.ClientLanguage != MapLanguageToClientLanguage(Configuration.SelectedLanguage)) return true;

            var testItemNames = new Dictionary<ClientLanguage, string>
            {
                { ClientLanguage.English, "Cobalt Ingot" },
                { ClientLanguage.Japanese, "コバルトインゴット" },
                { ClientLanguage.German, "Koboldeisenbarren" },
                { ClientLanguage.French, "Lingot de cobalt" }
            };

            var testItemName = testItemNames.GetValueOrDefault(clientState.ClientLanguage, "Cobalt Ingot");
            return (sheet?.GetRow(5059)?.Name ?? "") == testItemName;
        }

        public void GetItemInfoAndCopyToClipboard(uint itemId, string language)
        {
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

        private static readonly Lazy<HttpClient> LazyHttpClient = new(() => new HttpClient());

        private async Task GetItemNameByApi(uint itemId, string language)
        {
            if (itemId == 0)
            {
                ImGui.SetClipboardText("");
                return;
            }

            var filter = MapLanguageToFilter(language);
            string apiUrl = filter == "Name_chs" ?
                $"https://cafemaker.wakingsands.com/Item/{itemId}?columns={filter}" :
                $"https://xivapi.com/Item/{itemId}?columns={filter}";

            try
            {
                var jsonContent = await LazyHttpClient.Value.GetStringAsync(apiUrl);

                var match = Regex.Match(jsonContent, @":\""(.*?)\""}");

                string itemName = match.Groups[1].Value ?? "";

                if (filter == "Name_chs")
                {
                    itemName = Regex.Unescape(itemName);
                    if (isTraditionalChinese)
                    {
                        itemName = ChineseConverter.Convert(itemName, ChineseConversionDirection.SimplifiedToTraditional);
                    };
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

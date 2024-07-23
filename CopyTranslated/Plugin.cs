using CopyTranslated.Windows;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Networking.Http;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets2;
using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CopyTranslated
{
    public sealed partial class Plugin : IDalamudPlugin
    {
        public static string Name => "CopyTranslated";
        private const string CommandName = "/pcopy";

        private readonly IDalamudPluginInterface pluginInterface;
        private readonly ICommandManager commandManager;
        private readonly IChatGui chatGui;
        private readonly IGameGui gameGui;
        private readonly IContextMenu contextMenu;
        private readonly IClientState clientState;
        private readonly IDataManager dataManager;

        private readonly MenuItem gameObjectContextMenuItem;
        private readonly MenuItem inventoryContextMenuItem;

        public Configuration configuration { get; }
        public WindowSystem windowSystem { get; } = new("CopyTranslated");
        private readonly ConfigWindow configWindow;

        private bool useLuminaSheets = false;
        private ExcelSheet<Item>? itemSheet;
        private readonly Dictionary<string, string> itemCache = [];

        private uint hoveredItemId = 0;

        [GeneratedRegex(@":\""(.*?)\""}", RegexOptions.Compiled)]
        private static partial Regex jsonRegex();

        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectCallback = new HappyEyeballsCallback().ConnectCallback,
        })
        { Timeout = TimeSpan.FromSeconds(10) };

        public Plugin(
            IDalamudPluginInterface pluginInterface, 
            ICommandManager commandManager, 
            IChatGui chatGui, 
            IGameGui gameGui, 
            IContextMenu contextMenu, 
            IClientState clientState, 
            IDataManager dataManager)
        {
            this.pluginInterface = pluginInterface;
            this.commandManager = commandManager;
            this.chatGui = chatGui;
            this.gameGui = gameGui;
            this.contextMenu = contextMenu;
            this.clientState = clientState;
            this.dataManager = dataManager;

            configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            configuration.Initialize(pluginInterface);

            configWindow = new ConfigWindow(this);
            windowSystem.AddWindow(configWindow);

            pluginInterface.UiBuilder.Draw += DrawUI;
            pluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

            gameObjectContextMenuItem = new MenuItem 
            { 
                Name = "Copy Translated", 
                OnClicked = OnGameObjectMenuItemClicked, 
                UseDefaultPrefix = true 
            };
            inventoryContextMenuItem = new MenuItem 
            { 
                Name = "Copy Translated", 
                OnClicked = OnInventoryMenuItemClicked, 
                UseDefaultPrefix = true 
            };
            contextMenu.OnMenuOpened += OnContextMenuOpened;

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
            contextMenu.OnMenuOpened -= OnContextMenuOpened;
            windowSystem.RemoveAllWindows();
            configWindow.Dispose();
        }

        private void OnCommand(string command, string args) => configWindow.IsOpen = true;
        private void DrawUI() => windowSystem.Draw();
        public void DrawConfigUI() => configWindow.IsOpen = true;

        private void Initialize()
        {
            itemSheet = null;
            itemCache.Clear();
            useLuminaSheets = false;

            if (configuration.SelectedLanguage is "Chinese (Simplified)" or "Chinese (Traditional)") return;

            try
            {
                itemSheet = dataManager.GetExcelSheet<Item>(ConvertToClientLanguage(configuration.SelectedLanguage));

                var testItemNames = new Dictionary<ClientLanguage, string>
                {
                    { ClientLanguage.English, "Cobalt Ingot" },
                    { ClientLanguage.Japanese, "コバルトインゴット" },
                    { ClientLanguage.German, "Koboldeisenbarren" },
                    { ClientLanguage.French, "Lingot de cobalt" }
                };
                useLuminaSheets = (itemSheet?.GetRow(5059)?.Name ?? "") == testItemNames.GetValueOrDefault(clientState.ClientLanguage, "Cobalt Ingot");
            }
            catch { }
        }

        private static ClientLanguage ConvertToClientLanguage(string language) => language switch
        {
            "English" => ClientLanguage.English,
            "Japanese" => ClientLanguage.Japanese,
            "German" => ClientLanguage.German,
            "French" => ClientLanguage.French,
            _ => ClientLanguage.English
        };

        internal void OutputChatLine(SeString message)
        {
            SeStringBuilder sb = new();
            sb.AddUiForeground("[CT] ", 58).Append(message);

            chatGui.Print(new XivChatEntry { Message = sb.BuiltString });
        }

        private void OnContextMenuOpened(IMenuOpenedArgs args)
        {
            if (args.MenuType != ContextMenuType.Inventory)
            {
                hoveredItemId = (uint)gameGui.HoveredItem;
                if (hoveredItemId == 0 && args.AddonName != "ContentsInfoDetail") return;

                if (configuration.MultiLanguageMode)
                    AddMultiLanguageGameObjectMenuItems(args);
                else
                    args.AddMenuItem(gameObjectContextMenuItem);
            }
            else
            {
                if (configuration.MultiLanguageMode)
                    AddMultiLanguageInventoryMenuItems(args);
                else
                    args.AddMenuItem(inventoryContextMenuItem);
            }
        }

        private void AddMultiLanguageGameObjectMenuItems(IMenuOpenedArgs args)
        {
            foreach (var (language, isSelected) in configuration.SelectedLanguages)
            {
                if (isSelected)
                {
                    var itemid = hoveredItemId == 0 ? GetItemidFromContentsInfoDetail() : hoveredItemId;
                    args.AddMenuItem(new MenuItem
                    {
                        Name = $"Copy {language} name",
                        OnClicked = (_) => ProcessItemId(itemid, language),
                        UseDefaultPrefix = true
                    });
                }
            }
        }

        private void AddMultiLanguageInventoryMenuItems(IMenuOpenedArgs args)
        {
            foreach (var (language, isSelected) in configuration.SelectedLanguages)
            {
                var item = ((MenuTargetInventory)args.Target).TargetItem;
                if (item.HasValue)
                {
                    if (isSelected)
                        args.AddMenuItem(new MenuItem
                        {
                            Name = $"Copy {language} name",
                            OnClicked = (_) => ProcessItemId(item.Value.ItemId, language),
                            UseDefaultPrefix = true
                        });
                } 
                else 
                    OutputChatLine("Error: Cannot get itemid.");
            }
        }

        private void OnGameObjectMenuItemClicked(IMenuItemClickedArgs args) =>
            ProcessItemId(hoveredItemId == 0 ? GetItemidFromContentsInfoDetail() : hoveredItemId);

        private void OnInventoryMenuItemClicked(IMenuItemClickedArgs args)
        {
            var item = ((MenuTargetInventory)args.Target).TargetItem;
            if (item.HasValue) ProcessItemId(item.Value.ItemId);
        }

        private async void ProcessItemId(uint itemid)
        {
            itemid %= 500000;
            string itemName = await GetItemName(itemid).ConfigureAwait(false);
            ImGui.SetClipboardText(itemName);
            OutputChatLine($"Item copied: {itemName}");
        }

        private async void ProcessItemId(uint itemid, string language)
        {
            itemid %= 500000;
            string itemName = await GetItemName(itemid, language).ConfigureAwait(false);
            ImGui.SetClipboardText(itemName);
            OutputChatLine($"Item copied: {itemName}");
        }

        private unsafe uint GetItemidFromContentsInfoDetail()
        {
            UIModule* uiModule = (UIModule*)gameGui.GetUIModule();
            AgentModule* agents = uiModule->GetAgentModule();
            AgentInterface* agent = agents->GetAgentByInternalId(AgentId.ContentsTimer);

            return *(uint*)((nint)agent + 0x17CC);
        }

        private async Task<string> GetItemName(uint itemId)
        {
            return await GetItemName(itemId, configuration.SelectedLanguage).ConfigureAwait(false);
        }

        private async Task<string> GetItemName(uint itemId, string language)
        {
            string cacheKey = $"{itemId}_{language}";

            if (itemCache.TryGetValue(cacheKey, out var itemName)) 
                return itemName;

            if (useLuminaSheets && language is not "Chinese (Simplified)" and not "Chinese (Traditional)")
                itemName = dataManager.GetExcelSheet<Item>(ConvertToClientLanguage(language))?.GetRow(itemId)?.Name ?? "";

            if (string.IsNullOrEmpty(itemName))
                itemName = await GetItemNameFromApi(itemId, language).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(itemName))
                itemCache[cacheKey] = itemName;

            return itemName;
        }

        private async Task<string> GetItemNameFromApi(uint itemId, string language)
        {
            var filter = MapLanguageToFilter(language);
            string apiUrl = filter == "Name_chs" ?
                $"https://cafemaker.wakingsands.com/Item/{itemId}?columns={filter}" :
                $"https://xivapi.com/Item/{itemId}?columns={filter}";

            try
            {
                var jsonContent = await HttpClient.GetStringAsync(apiUrl).ConfigureAwait(false);
                var match = jsonRegex().Match(jsonContent);
                string itemName = Regex.Unescape(match.Groups[1].Value ?? "");

                if (language == "Chinese (Traditional)")
                    itemName = ChineseConverter.Convert(itemName, ChineseConversionDirection.SimplifiedToTraditional);

                return string.IsNullOrEmpty(itemName) ? throw new Exception("API response failed.") : itemName;
            }
            catch (Exception ex)
            {
                OutputChatLine($"Error: {ex.Message}");
                return "";
            }
        }

        private static string MapLanguageToFilter(string language) => language switch
        {
            "English" => "Name_en",
            "Japanese" => "Name_ja",
            "German" => "Name_de",
            "French" => "Name_fr",
            "Chinese (Simplified)" => "Name_chs",
            "Chinese (Traditional)" => "Name_chs",
            _ => "Name_en"
        };
    }
}

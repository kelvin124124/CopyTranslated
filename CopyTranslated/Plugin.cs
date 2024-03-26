using CopyTranslated.Windows;
using Dalamud;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Networking.Http;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets2;
using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;
using System;
using System.Collections.Generic;
using System.Net;
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
        private readonly Dictionary<uint, string> itemCache = [];

        private uint hoveredItemId = 0;

        private static readonly HttpClient HttpClient =
        new(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = new HappyEyeballsCallback().ConnectCallback,
        })
        { Timeout = TimeSpan.FromSeconds(10) };

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] ICommandManager commandManager,
            [RequiredVersion("1.0")] IChatGui chatGui,
            [RequiredVersion("1.0")] IGameGui gameGui,
            [RequiredVersion("1.0")] IContextMenu contextMenu,
            [RequiredVersion("1.0")] IClientState clientState,
            [RequiredVersion("1.0")] IDataManager dataManager)
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
                OnClicked = OnGameObjectMenuItemClicked
            };
            inventoryContextMenuItem = new MenuItem
            {
                Name = "Copy Translated",
                OnClicked = OnInventoryMenuItemClicked
            };
            contextMenu.OnMenuOpened += OnContextMenuOpened;


            commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open copy translated configuration window."
            });

            Initialize();
            configWindow.OnLanguageChanged += Initialize;
        }
        private void OnContextMenuOpened(MenuOpenedArgs args)
        {
            if (args.MenuType != ContextMenuType.Inventory)
            {
                hoveredItemId = (uint)gameGui.HoveredItem;
                if (hoveredItemId == 0 && args.AddonName != "ContentsInfoDetail")
                    return;
                args.AddMenuItem(gameObjectContextMenuItem);
            }
            else
            {
                args.AddMenuItem(inventoryContextMenuItem);
            }
        }

        public void Dispose()
        {
            configWindow.OnLanguageChanged -= Initialize;

            contextMenu.OnMenuOpened -= OnContextMenuOpened;

            windowSystem.RemoveAllWindows();
            configWindow.Dispose();
        }
        private void OnCommand(string command, string args)
        {
            configWindow.IsOpen = true;
        }

        private void DrawUI() => windowSystem.Draw();

        public void DrawConfigUI() => configWindow.IsOpen = true;

        private void Initialize()
        {
            itemSheet = null;
            itemCache.Clear();
            useLuminaSheets = false;

            if (configuration.SelectedLanguage == "Chinese (Simplified)" || configuration.SelectedLanguage == "Chinese (Traditional)")
                return;

            try
            {
                var sheet = dataManager.GetExcelSheet<Item>(ConvertToClientLanguage(configuration.SelectedLanguage));

                var testItemNames = new Dictionary<ClientLanguage, string>
                {
                    { ClientLanguage.English, "Cobalt Ingot" },
                    { ClientLanguage.Japanese, "コバルトインゴット" },
                    { ClientLanguage.German, "Koboldeisenbarren" },
                    { ClientLanguage.French, "Lingot de cobalt" }
                };
                var testItemName = testItemNames.GetValueOrDefault(clientState.ClientLanguage, "Cobalt Ingot");
                if ((sheet?.GetRow(5059)?.Name ?? "") == testItemName)
                    useLuminaSheets = true;
            }
            catch { return; }
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

        private void OnGameObjectMenuItemClicked(MenuItemClickedArgs args)
        {
            uint itemid = hoveredItemId;
            if (hoveredItemId == 0)
                itemid = GetItemidFromContentsInfoDetail();

            ProcessItemId(itemid);
        }

        private void OnInventoryMenuItemClicked(MenuItemClickedArgs args)
        {
            var target = (MenuTargetInventory)args.Target;
            var item = target.TargetItem;
            if (item.HasValue)
                ProcessItemId(item.Value.ItemId);
        }

        private async void ProcessItemId(uint itemid)
        {
            itemid %= 500000;
            string itemName = await GetItemName(itemid).ConfigureAwait(false);
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
            if (itemCache.TryGetValue(itemId, out var itemName))
                return itemName;

            if (useLuminaSheets && itemSheet != null)
                itemName = itemSheet?.GetRow(itemId)?.Name ?? "";

            if (itemName.IsNullOrEmpty())
                itemName = await GetItemNameFromApi(itemId, configuration.SelectedLanguage).ConfigureAwait(false);

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

                var match = Regex.Match(jsonContent, @":\""(.*?)\""}", RegexOptions.Compiled);

                string itemName = match.Groups[1].Value ?? "";
                itemName = Regex.Unescape(itemName);

                if (language == "Chinese (Traditional)")
                    itemName = ChineseConverter.Convert(itemName, ChineseConversionDirection.SimplifiedToTraditional);

                if (string.IsNullOrEmpty(itemName))
                {
                    throw new Exception("API request failed.");
                }
                else
                {
                    return itemName;
                }
            }
            catch (Exception ex)
            {
                OutputChatLine($"Error: {ex.Message}");
                return "";
            }
        }

        private static string MapLanguageToFilter(string language)
        {
            return language switch
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
}

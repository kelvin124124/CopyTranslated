using CopyTranslated.Windows;
using Dalamud.ContextMenu;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Flurl.Http;
using ImGuiNET;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace CopyTranslated
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Item Translator Plugin";

        private DalamudPluginInterface PluginInterface { get; init; }
        private CommandManager CommandManager { get; init; }
        public Configuration Configuration { get; init; }
        public WindowSystem WindowSystem = new("ItemTranslatorPlugin");

        private ConfigWindow ConfigWindow { get; init; }

        public GameGui GameGui { get; init; } = null!;
        public ChatGui ChatGui { get; init; }

        private DalamudContextMenu contextMenu;
        private GameObjectContextMenuItem gameObjectContextMenuItem;
        private InventoryContextMenuItem inventoryContextMenuItem;

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            [RequiredVersion("1.0")] ChatGui chatGui)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.ChatGui = chatGui;

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            ConfigWindow = new ConfigWindow(this);

            WindowSystem.AddWindow(ConfigWindow);

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

            // create instance
            contextMenu = new DalamudContextMenu();

            // create context menu item for GameObject
            gameObjectContextMenuItem = new GameObjectContextMenuItem(
                new SeString(new TextPayload("Copy Translated")), // text
                Lookup, // action to invoke
                true); // use dalamud indicator

            contextMenu.OnOpenGameObjectContextMenu += OpenGameObjectContextMenu;

            // create context menu item for Inventory
            inventoryContextMenuItem = new InventoryContextMenuItem(
                new SeString(new TextPayload("Copy Translated")), // text
                InventoryLookup, // new action for inventory lookup
                true); // use dalamud indicator

            contextMenu.OnOpenInventoryContextMenu += OpenInventoryContextMenu;
        }

        public void Dispose()
        {
            contextMenu.OnOpenGameObjectContextMenu -= OpenGameObjectContextMenu;
            contextMenu.OnOpenInventoryContextMenu -= OpenInventoryContextMenu;
            contextMenu.Dispose();

            this.WindowSystem.RemoveAllWindows();
            ConfigWindow.Dispose();
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
        }

        public void DrawConfigUI()
        {
            ConfigWindow.IsOpen = true;
        }

        internal void OutputChatLine(SeString message)
        {
            SeStringBuilder sb = new();
            _ = sb.AddUiForeground(45);
            _ = sb.AddText("[Item Translated] ");
            _ = sb.AddUiForegroundOff();
            _ = sb.Append(message);
            this.ChatGui.PrintChat(new XivChatEntry
            {
                Message = sb.BuiltString
            });
        }

        private void OpenGameObjectContextMenu(GameObjectContextMenuOpenArgs args)
        {
            if (args.ObjectWorld != 0)
            {
                return;
            }
            args.AddCustomItem(gameObjectContextMenuItem);
        }

        private void OpenInventoryContextMenu(InventoryContextMenuOpenArgs args)
        {
            var itemId = args.ItemId;
            if (itemId == 0) OutputChatLine($"Error: code 01 at {string.Format("{0:HH:mm:ss tt}", DateTime.Now)}");

            args.AddCustomItem(inventoryContextMenuItem);
        }

        private async void Lookup(GameObjectContextMenuItemSelectedArgs args)
        {
            if (this.GameGui == null) OutputChatLine($"Error: code 02 at {string.Format("{0:HH:mm:ss tt}", DateTime.Now)}");
            var itemId = (uint)(this.GameGui?.HoveredItem ?? 0);

            if (itemId == 0) OutputChatLine($"Error: code 03 at {string.Format("{0:HH:mm:ss tt}", DateTime.Now)}");

            var language = MapLanguageToAbbreviation(Configuration.SelectedLanguage);

            await GetItemInfoAndCopyToClipboard(itemId, language);
        }

        private async void InventoryLookup(InventoryContextMenuItemSelectedArgs args)
        {
            var itemId = args.ItemId;
            var language = MapLanguageToAbbreviation(Configuration.SelectedLanguage);

            await GetItemInfoAndCopyToClipboard(itemId, language);
        }

        private string MapLanguageToAbbreviation(string fullLanguageName)
        {
            return fullLanguageName switch
            {
                "English" => "en",
                "Japanese" => "ja",
                // Add other mappings as needed
                _ => "en" // default to English if not found
            };
        }

        private async Task GetItemInfoAndCopyToClipboard(uint itemId, string language)
        {
            try
            {
                var apiUrl = $"https://xivapi.com/Item/{itemId}?columns=Name_{language}";
                var jsonContent = await apiUrl.GetStringAsync();
                dynamic item = JsonConvert.DeserializeObject(jsonContent);

                // Assuming the Name attribute is in the format "Name_en", "Name_ja", etc.
                var itemNameAttribute = $"Name_{language}";
                string itemName = item?[itemNameAttribute];

                if (string.IsNullOrEmpty(itemName))
                {
                    OutputChatLine($"Error: code 04 at " +
                        $"{string.Format("{0:HH:mm:ss tt}", DateTime.Now)} " +
                        $"https://xivapi.com/Item/{itemId}?columns=Name_{language} returned " +
                        $"{jsonContent}");
                }
                else
                {
                    ImGui.SetClipboardText(itemName);
                    OutputChatLine("Item name copied!");
                }
            }
            catch (Exception ex)
            {
                // Handle exception by logging the message to the clipboard
                OutputChatLine($"Error: {ex.Message}");
            }
        }
    }
}

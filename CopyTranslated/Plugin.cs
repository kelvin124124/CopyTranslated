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
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
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
            [RequiredVersion("1.0")] ChatGui chatGui,
            [RequiredVersion("1.0")] GameGui gameGui)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.ChatGui = chatGui;
            this.GameGui = gameGui;

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
            // avoid selecting players
            if (args.ObjectWorld != 0) return;
            args.AddCustomItem(gameObjectContextMenuItem);
        }

        private void OpenInventoryContextMenu(InventoryContextMenuOpenArgs args)
        {
            if (args.ItemId == 0) OutputChatLine("Error: inventory ItemID = 0");
            args.AddCustomItem(inventoryContextMenuItem);
        }

        private unsafe void Lookup(GameObjectContextMenuItemSelectedArgs args)
        {
            uint itemId = 0;

            switch (args.ParentAddonName)
            {
                case "RecipeNote":
                    nint recipeNoteAgent = this.GameGui.FindAgentInterface(args.ParentAddonName);
                    unsafe { itemId = *(uint*)(recipeNoteAgent + 0x398); }
                    break;

                case "RecipeTree":
                case "RecipeMaterialList":
                    unsafe
                    {
                        
                        UIModule* uiModule = (UIModule*)this.GameGui.GetUIModule();
                        AgentModule* agents = uiModule->GetAgentModule();
                        AgentInterface* agent = agents->GetAgentByInternalId(AgentId.RecipeItemContext);

                        itemId = *(uint*)((nint)agent + 0x28);
                    }
                    break;

                case "ChatLog":
                    nint ChatLog = this.GameGui.FindAgentInterface(args.ParentAddonName);
                    unsafe { itemId = *(uint*)(ChatLog + 0x948); }
                    break;

                case "ContentsInfoDetail":
                    unsafe
                    {
                        
                        UIModule* uiModule = (UIModule*)this.GameGui.GetUIModule();
                        AgentModule* agents = uiModule->GetAgentModule();
                        AgentInterface* agent = agents->GetAgentByInternalId(AgentId.ContentsTimer);
                        
                        itemId = *(uint*)((nint)agent + 0x17CC);
                    }
                    break;

                case "DailyQuestSupply":
                    nint DailyQuestSupply = this.GameGui.FindAgentInterface(args.ParentAddonName);
                    unsafe { itemId = *(uint*)(DailyQuestSupply + 0x54); }
                    break;

                default:
                    itemId = (uint)this.GameGui.HoveredItem;
                    if (itemId == 0) OutputChatLine($"Error: {itemId},{args.ParentAddonName}\nReport to developer.");
                    break;
            }
            var language = MapLanguageToAbbreviation(Configuration.SelectedLanguage);
            Task.Run(() => GetItemInfoAndCopyToClipboard(itemId, language));
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
                if (itemId == 0) return;

                var apiUrl = $"https://xivapi.com/Item/{itemId}?columns=Name_{language}";
                var jsonContent = await apiUrl.GetStringAsync();
                dynamic item = JsonConvert.DeserializeObject(jsonContent);

                var itemNameAttribute = $"Name_{language}";
                string itemName = item?[itemNameAttribute];

                if (string.IsNullOrEmpty(itemName))
                {
                    OutputChatLine($"Error: API error at " +
                        $"{string.Format("{0:HH:mm:ss tt}", DateTime.Now)} " +
                        $"https://xivapi.com/Item/{itemId}?columns=Name_{language} returned " +
                        $"{jsonContent}");
                }
                else
                {
                    ImGui.SetClipboardText(itemName);
                    OutputChatLine($"Item copied: {itemName}");
                }
            }
            catch (Exception ex) { OutputChatLine($"Error: {ex.Message}"); }
        }
    }
}

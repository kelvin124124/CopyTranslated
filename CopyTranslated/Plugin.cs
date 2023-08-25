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

        private readonly DalamudPluginInterface pluginInterface;
        private readonly CommandManager commandManager;
        private readonly ChatGui chatGui;
        private readonly GameGui gameGui;
        private readonly DalamudContextMenu contextMenu;
        private readonly GameObjectContextMenuItem gameObjectContextMenuItem;
        private readonly InventoryContextMenuItem inventoryContextMenuItem;
        public Configuration Configuration { get; }
        public WindowSystem WindowSystem { get; } = new("ItemTranslatorPlugin");
        private readonly ConfigWindow configWindow;

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            [RequiredVersion("1.0")] ChatGui chatGui,
            [RequiredVersion("1.0")] GameGui gameGui)
        {
            this.pluginInterface = pluginInterface;
            this.commandManager = commandManager;
            this.chatGui = chatGui;
            this.gameGui = gameGui;

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
        }

        public void Dispose()
        {
            contextMenu.OnOpenGameObjectContextMenu -= OpenGameObjectContextMenu;
            contextMenu.OnOpenInventoryContextMenu -= OpenInventoryContextMenu;
            contextMenu.Dispose();

            WindowSystem.RemoveAllWindows();
            configWindow.Dispose();
        }

        private void DrawUI() => WindowSystem.Draw();

        public void DrawConfigUI() => configWindow.IsOpen = true;

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

            Task.Run(() => GetItemInfoAndCopyToClipboard(itemId, MapLanguageToAbbreviation(Configuration.SelectedLanguage)));
        }

        private async void InventoryLookup(InventoryContextMenuItemSelectedArgs args)
        {
            await GetItemInfoAndCopyToClipboard(args.ItemId, MapLanguageToAbbreviation(Configuration.SelectedLanguage));
        }

        private static string MapLanguageToAbbreviation(string fullLanguageName) => fullLanguageName switch
        {
            "English" => "en",
            "Japanese" => "ja",
            "German" => "de",
            "French" => "fr",
            _ => "en"
        };

        private async Task GetItemInfoAndCopyToClipboard(uint itemId, string language)
        {
            try
            {
                if (itemId == 0) return;

                var apiUrl = $"https://xivapi.com/Item/{itemId}?columns=Name_{language}";
                var jsonContent = await apiUrl.GetStringAsync();
                dynamic? item = JsonConvert.DeserializeObject(jsonContent);

                string? itemName = item?[$"Name_{language}"];

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
            catch (Exception ex) { OutputChatLine($"Error: {ex.Message}"); }
        }
    }
}

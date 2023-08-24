using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace CopyTranslated.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private Configuration configuration;

        public ConfigWindow(Plugin plugin) : base(
            "A Wonderful Configuration Window",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse)
        {
            this.Size = new Vector2(232, 75);
            this.SizeCondition = ImGuiCond.Always;

            this.configuration = plugin.Configuration;
        }

        public void Dispose() { }

        public override void Draw()
        {
            // Dropdown for language selection
            var languages = new string[] { "English", "Japanese" };
            int currentLanguageIndex = Array.IndexOf(languages, this.configuration.SelectedLanguage);
            if (currentLanguageIndex == -1) currentLanguageIndex = 0; // Default to English

            if (ImGui.Combo("Language", ref currentLanguageIndex, languages, languages.Length))
            {
                this.configuration.SelectedLanguage = languages[currentLanguageIndex];
                this.configuration.Save();
            }
        }
    }
}

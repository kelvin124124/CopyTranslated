using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Numerics;

namespace CopyTranslated.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Configuration configuration;
        private readonly string[] languages = { "English", "Japanese", "German", "French", "Chinese (Simplified)", "Chinese (Traditional)" };

        public event Action? OnLanguageChanged;

        public ConfigWindow(Plugin plugin) : base(
            "Copy Translated config",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse)
        {
            Size = new Vector2(232, 75);
            SizeCondition = ImGuiCond.Always;

            configuration = plugin.Configuration;
        }

        public void Dispose() { }

        public override void Draw()
        {
            DrawLanguageDropdown();
        }

        private void DrawLanguageDropdown()
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Language");
            ImGui.SameLine();

            int currentLanguageIndex = Array.IndexOf(languages, configuration.SelectedLanguage);
            if (currentLanguageIndex == -1) currentLanguageIndex = 0;

            if (ImGui.Combo("##LanguageCombo", ref currentLanguageIndex, languages, languages.Length))
            {
                configuration.SelectedLanguage = languages[currentLanguageIndex];
                configuration.Save();
                OnLanguageChanged?.Invoke();
            }
        }
    }
}

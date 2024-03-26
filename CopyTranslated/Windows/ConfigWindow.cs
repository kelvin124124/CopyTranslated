using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Numerics;

namespace CopyTranslated.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Configuration configuration;
        private readonly string[] supportedLanguages = ["English", "Japanese", "German", "French", "Chinese (Simplified)", "Chinese (Traditional)"];

        public event Action? OnLanguageChanged;

        public ConfigWindow(Plugin plugin) : base(
            "Copy Translated config",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse)
        {
            Size = new Vector2(232, 75);
            SizeCondition = ImGuiCond.Always;

            configuration = plugin.configuration;
        }

        public void Dispose() { }

        public override void Draw()
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Language");
            ImGui.SameLine();

            int currentLanguageIndex = Array.IndexOf(supportedLanguages, configuration.SelectedLanguage);
            if (currentLanguageIndex == -1) currentLanguageIndex = 0;

            if (ImGui.Combo("##LanguageCombo", ref currentLanguageIndex, supportedLanguages, supportedLanguages.Length))
            {
                configuration.SelectedLanguage = supportedLanguages[currentLanguageIndex];
                configuration.Save();
                OnLanguageChanged?.Invoke();
            }
        }
    }
}

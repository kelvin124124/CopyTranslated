using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Numerics;

namespace CopyTranslated.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Configuration configuration;
        private static readonly string[] SupportedLanguages = ["English", "Japanese", "German", "French", "Chinese (Simplified)", "Chinese (Traditional)"];

        public event Action? OnLanguageChanged;

        public ConfigWindow(Plugin plugin) : base(
            "Copy Translated config",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            Size = new Vector2(230, 220);
            SizeCondition = ImGuiCond.Always;
            configuration = plugin.configuration;
        }

        public void Dispose() { }

        public override void Draw()
        {
            bool _MultiLanguageMode = configuration.MultiLanguageMode;
            if (ImGui.Checkbox("Multi-language mode", ref _MultiLanguageMode))
            {
                configuration.MultiLanguageMode = _MultiLanguageMode;
                configuration.Save();
                OnLanguageChanged?.Invoke();
            }

            ImGui.Separator();
            if (configuration.MultiLanguageMode) 
            {
                foreach (var language in SupportedLanguages)
                {
                    bool isSelected = configuration.SelectedLanguages[language];
                    if (ImGui.Checkbox($"{language}##Language", ref isSelected))
                    {
                        configuration.SelectedLanguages[language] = isSelected;
                        configuration.Save();
                        OnLanguageChanged?.Invoke();
                    }
                }
            }
            else
            {
                ImGui.AlignTextToFramePadding();
                ImGui.Text("Language");
                ImGui.SameLine();

                int currentLanguageIndex = Array.IndexOf(SupportedLanguages, configuration.SelectedLanguage);
                if (ImGui.Combo("##LanguageCombo", ref currentLanguageIndex, SupportedLanguages, SupportedLanguages.Length))
                {
                    configuration.SelectedLanguage = SupportedLanguages[currentLanguageIndex];
                    configuration.Save();
                    OnLanguageChanged?.Invoke();
                }
            }
        }
    }
}

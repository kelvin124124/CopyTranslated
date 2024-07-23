using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace CopyTranslated
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;
        public string SelectedLanguage { get; set; } = "English";
        public bool MultiLanguageMode { get; set; } = false;
        public Dictionary<string, bool> SelectedLanguages { get; set; } = new()
        {
            ["English"] = true,
            ["Japanese"] = false,
            ["German"] = false,
            ["French"] = false,
            ["Chinese (Simplified)"] = false,
            ["Chinese (Traditional)"] = false
        };

        [NonSerialized]
        private IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface) => this.pluginInterface = pluginInterface;

        public void Save() => pluginInterface?.SavePluginConfig(this);
    }
}

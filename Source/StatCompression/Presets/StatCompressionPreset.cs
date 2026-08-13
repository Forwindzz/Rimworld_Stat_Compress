using System.Collections.Generic;

namespace StatCompression
{
    internal sealed class StatCompressionPreset
    {
        public string Name;
        public string FileName;
        public string Path;
        public bool BuiltIn;
        public List<StatCompressionStatConfig> Configs = new List<StatCompressionStatConfig>();
    }

    internal sealed class StatCompressionPresetConflict
    {
        public string PresetName;
        public string DefName;
        public string Fields;
    }
}

using System.Collections.Generic;
using Verse;

namespace StatCompression
{
    internal sealed class StatCompressionPreset
    {
        public string Name;
        public string LabelKey;
        public string FileName;
        public string Path;
        public bool BuiltIn;
        public List<StatCompressionStatConfig> Configs = new List<StatCompressionStatConfig>();

        public string DisplayName =>
            !LabelKey.NullOrEmpty() && LabelKey.CanTranslate()
                ? LabelKey.Translate().ToString()
                : Name;
    }

    internal sealed class StatCompressionPresetConflict
    {
        public string PresetName;
        public string DefName;
        public string Fields;
    }
}

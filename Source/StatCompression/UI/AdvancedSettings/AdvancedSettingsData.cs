using System;
using System.Collections.Generic;

namespace StatCompression
{
    internal enum AdvancedDataSourceKind
    {
        Settings,
        Preset
    }

    internal readonly struct AdvancedDataSet
    {
        public AdvancedDataSet(
            AdvancedDataSourceKind kind,
            object sourceToken,
            int structureVersion,
            string name,
            IReadOnlyList<StatCompressionStatConfig> configs)
        {
            Kind = kind;
            SourceToken = sourceToken;
            StructureVersion = structureVersion;
            Name = name;
            Configs = configs;
        }

        public AdvancedDataSourceKind Kind { get; }
        public object SourceToken { get; }
        public int StructureVersion { get; }
        public string Name { get; }
        public IReadOnlyList<StatCompressionStatConfig> Configs { get; }
    }

    [Flags]
    internal enum AdvancedConfigField
    {
        None = 0,
        Enabled = 1 << 0,
        Method = 1 << 1,
        TScale = 1 << 2,
        Baseline = 1 << 3,
        Threshold = 1 << 4,
        Direction = 1 << 5,
        Metadata = 1 << 6,
        AllValues = Enabled | Method | TScale | Baseline | Threshold | Direction
    }

    internal readonly struct AdvancedConfigUpdate
    {
        public AdvancedConfigUpdate(
            StatCompressionStatConfig config,
            AdvancedConfigField fields)
        {
            Config = config;
            Fields = fields;
        }

        public StatCompressionStatConfig Config { get; }
        public AdvancedConfigField Fields { get; }
    }

    internal readonly struct GlobalCompressionInput
    {
        public GlobalCompressionInput(StatCompressionSettings settings)
        {
            Method = settings.method;
            Parameter = settings.parameter;
            ThresholdFactor = settings.thresholdFactor;
        }

        public CompressionMethod Method { get; }
        public float Parameter { get; }
        public float ThresholdFactor { get; }
    }
}

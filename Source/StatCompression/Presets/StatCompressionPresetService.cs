using System;
using System.Collections.Generic;
using Verse;

namespace StatCompression
{
    internal sealed class PresetUiSnapshot
    {
        private readonly HashSet<string> activePresetNames;
        private readonly Dictionary<string, StatCompressionPresetConflict> conflictsByPresetName;

        public PresetUiSnapshot(
            HashSet<string> activePresetNames,
            Dictionary<string, StatCompressionPresetConflict> conflictsByPresetName)
        {
            this.activePresetNames = activePresetNames;
            this.conflictsByPresetName = conflictsByPresetName;
        }

        public bool IsActive(string fileName)
        {
            return activePresetNames.Contains(fileName);
        }

        public bool TryGetConflict(
            string fileName,
            out StatCompressionPresetConflict conflict)
        {
            return conflictsByPresetName.TryGetValue(fileName, out conflict);
        }
    }

    internal static class StatCompressionPresetService
    {
        private const float FloatTolerance = 0.000001f;

        private static int activeRevision;
        private static int cachedPresetRevision = -1;
        private static int cachedActiveRevision = -1;
        private static int cachedActiveCount = -1;
        private static int cachedActiveHash;
        private static PresetUiSnapshot cachedSnapshot;

        public static PresetUiSnapshot GetUiSnapshot(StatCompressionSettings settings)
        {
            var presets = StatCompressionPresetRepository.Presets;
            var activeCount = 0;
            var activeHash = 0;
            for (var i = 0; i < settings.activePresets.Count; i++)
            {
                var name = settings.activePresets[i];
                if (!name.NullOrEmpty())
                {
                    activeCount++;
                    activeHash ^= StringComparer.OrdinalIgnoreCase.GetHashCode(name);
                }
            }

            if (cachedSnapshot != null &&
                cachedPresetRevision == StatCompressionPresetRepository.Revision &&
                cachedActiveRevision == activeRevision &&
                cachedActiveCount == activeCount &&
                cachedActiveHash == activeHash)
            {
                return cachedSnapshot;
            }

            var activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < settings.activePresets.Count; i++)
            {
                var name = settings.activePresets[i];
                if (!name.NullOrEmpty())
                {
                    activeNames.Add(name);
                }
            }

            var activeIndexes = new List<ActivePresetIndex>();
            for (var i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                if (activeNames.Contains(preset.FileName))
                {
                    activeIndexes.Add(new ActivePresetIndex(preset));
                }
            }

            var conflicts = new Dictionary<string, StatCompressionPresetConflict>(
                StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < presets.Count; i++)
            {
                var candidate = presets[i];
                if (!activeNames.Contains(candidate.FileName) &&
                    TryFindConflict(candidate, activeIndexes, out var conflict))
                {
                    conflicts.Add(candidate.FileName, conflict);
                }
            }

            cachedPresetRevision = StatCompressionPresetRepository.Revision;
            cachedActiveRevision = activeRevision;
            cachedActiveCount = activeCount;
            cachedActiveHash = activeHash;
            cachedSnapshot = new PresetUiSnapshot(activeNames, conflicts);
            return cachedSnapshot;
        }

        public static void NotifyActivePresetsChanged()
        {
            activeRevision++;
        }

        internal static string[] DifferentFields(
            StatCompressionStatConfig left,
            StatCompressionStatConfig right)
        {
            var fields = new List<string>();
            if (left.enabled != right.enabled) fields.Add("enabled");
            if (left.method != right.method) fields.Add("method");
            if (!NearlyEqual(left.method_t, right.method_t)) fields.Add("method_t");
            if (!NearlyEqual(left.tScale, right.tScale)) fields.Add("tScale");
            if (!NearlyEqual(left.baseline, right.baseline)) fields.Add("baseline");
            if (!NearlyEqual(left.thresholdFactor, right.thresholdFactor)) fields.Add("thresholdFactor");
            if (left.direction != right.direction) fields.Add("direction");
            return fields.ToArray();
        }

        private static bool TryFindConflict(
            StatCompressionPreset candidate,
            List<ActivePresetIndex> activeIndexes,
            out StatCompressionPresetConflict conflict)
        {
            for (var i = 0; i < activeIndexes.Count; i++)
            {
                var active = activeIndexes[i];
                for (var j = 0; j < candidate.Configs.Count; j++)
                {
                    var config = candidate.Configs[j];
                    if (config != null &&
                        active.ConfigsByDefName.TryGetValue(config.defName, out var other))
                    {
                        var fields = DifferentFields(config, other);
                        if (fields.Length > 0)
                        {
                            conflict = new StatCompressionPresetConflict
                            {
                                PresetName = active.Preset.DisplayName,
                                DefName = config.defName,
                                Fields = string.Join(", ", fields)
                            };
                            return true;
                        }
                    }
                }
            }

            conflict = null;
            return false;
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= FloatTolerance;
        }

        private sealed class ActivePresetIndex
        {
            public ActivePresetIndex(StatCompressionPreset preset)
            {
                Preset = preset;
                ConfigsByDefName = new Dictionary<string, StatCompressionStatConfig>(
                    StringComparer.Ordinal);
                for (var i = 0; i < preset.Configs.Count; i++)
                {
                    var config = preset.Configs[i];
                    if (config != null &&
                        !config.defName.NullOrEmpty() &&
                        !ConfigsByDefName.ContainsKey(config.defName))
                    {
                        ConfigsByDefName.Add(config.defName, config);
                    }
                }
            }

            public StatCompressionPreset Preset { get; }
            public Dictionary<string, StatCompressionStatConfig> ConfigsByDefName { get; }
        }
    }
}

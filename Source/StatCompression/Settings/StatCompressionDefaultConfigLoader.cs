using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionDefaultConfigLoader
    {
        private const string RelativePath = "Data/DefaultSettings.xml";

        private static DefaultSettingsPreset preset;

        public static bool TryGet(string defName, out DefaultStatConfigRecord record)
        {
            EnsureLoaded();
            return preset.recordsByDefName.TryGetValue(defName, out record);
        }

        public static void ApplyGlobalDefaults(StatCompressionSettings settings)
        {
            EnsureLoaded();
            var global = preset.global;
            settings.enabled = global.enabled;
            settings.showInfoCardSettingsButton = global.showInfoCardSettingsButton;
            settings.stage = global.stage;
            settings.autoFallbackToGlobalPostfix = global.autoFallbackToGlobalPostfix;
            settings.method = global.method;
            settings.parameter = global.parameter;
            settings.thresholdFactor = global.thresholdFactor;
            settings.BodyPartHealthConfig.CopyFrom(preset.bodyPartHealthConfig);
            var targetDamageConfigs = settings.SpecialDamageConfigs;
            for (var i = 0; i < targetDamageConfigs.Count; i++)
            {
                var source = preset.specialDamageConfigs[i];
                settings.GetAdvancedConfig(source.defName).CopyFrom(source);
            }
            var targetHediffStageConfigs = settings.SpecialHediffStageConfigs;
            for (var i = 0; i < targetHediffStageConfigs.Count; i++)
            {
                var source = preset.specialHediffStageConfigs[i];
                settings.GetAdvancedConfig(source.defName).CopyFrom(source);
            }
        }

        private static void EnsureLoaded()
        {
            if (preset != null)
            {
                return;
            }

            preset = LoadPreset();
        }

        private static DefaultSettingsPreset LoadPreset()
        {
            var result = new DefaultSettingsPreset();
            var contentPack = StatCompressionMod.ContentPack;
            if (contentPack == null)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Cannot load default settings before ModContentPack is available.");
                return result;
            }

            var path = Path.Combine(contentPack.RootDir, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Default settings XML not found: {path}");
                return result;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(path);
            }
            catch (Exception ex)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Failed to read default settings XML: {path}\n{ex}");
                return result;
            }

            if (!StatCompressionSettingsXml.TryGetRoot(document, out var root, out var rootError))
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Invalid default settings XML ({rootError}): {path}");
                return result;
            }

            StatCompressionSettingsXml.ReadGlobal(root.Element("Global"), result.global);
            StatCompressionSettingsXml.ReadBodyPartHealth(
                root.Element("BodyPartHealth"),
                result.bodyPartHealthConfig);
            _ = StatCompressionSettingsXml.ReadSpecialDamageConfigs(
                root.Element("SpecialDamageConfigs"),
                result.specialDamageConfigs);
            _ = StatCompressionSettingsXml.ReadSpecialHediffStageConfigs(
                root.Element("SpecialHediffStageConfigs"),
                result.specialHediffStageConfigs);
            var statsElement = root.Element("Stats");
            if (statsElement != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var warnedDuplicates = new HashSet<string>(StringComparer.Ordinal);
                foreach (var element in statsElement.Elements("Stat"))
                {
                    if (!StatCompressionSettingsXml.TryReadDefaultStat(element, out var record, out var error))
                    {
                        Log.Warning($"[{StatCompressionConstants.DisplayName}] Skipping invalid default stat config: {error}");
                        continue;
                    }

                    if (!seen.Add(record.defName))
                    {
                        if (warnedDuplicates.Add(record.defName))
                        {
                            Log.Warning($"[{StatCompressionConstants.DisplayName}] Duplicate default StatDef ignored: {record.defName}");
                        }

                        continue;
                    }

                    result.recordsByDefName.Add(record.defName, record);
                }
            }

            Log.Message($"[{StatCompressionConstants.DisplayName}] Loaded default settings XML: stats={result.recordsByDefName.Count}, path={path}");
            return result;
        }
    }

    internal sealed class DefaultSettingsPreset
    {
        public readonly DefaultGlobalSettings global = new DefaultGlobalSettings();
        public readonly StatCompressionStatConfig bodyPartHealthConfig =
            SpecialCompressionConfigs.CreateBodyPartHealth();
        public readonly List<StatCompressionStatConfig> specialDamageConfigs =
            SpecialCompressionConfigs.CreateDamageConfigs();
        public readonly List<StatCompressionStatConfig> specialHediffStageConfigs =
            SpecialCompressionConfigs.CreateHediffStageConfigs();
        public readonly Dictionary<string, DefaultStatConfigRecord> recordsByDefName =
            new Dictionary<string, DefaultStatConfigRecord>(StringComparer.Ordinal);
    }
}

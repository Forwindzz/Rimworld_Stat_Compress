using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace StatCompression
{
    public sealed partial class StatCompressionSettings : ModSettings
    {
        private const float DefaultMaxValue = 9999999f;
        private const int CurrentSettingsVersion = 2;

        private StatCompressionStatConfig[] configByIndex;
        private bool initialized;
        private int settingsVersion;

        public bool enabled = true;
        public bool showInfoCardSettingsButton = true;
        public CompressionStage stage = CompressionStage.BeforePostProcessCurve;
        public bool autoFallbackToGlobalPostfix = true;
        public CompressionMethod method = CompressionMethod.Logarithmic;
        public float parameter = 2f;
        public float thresholdFactor = 1f;
        public StatCompressionStatConfig bodyPartHealthConfig = SpecialCompressionConfigs.CreateBodyPartHealth();
        public List<StatCompressionStatConfig> specialDamageConfigs = SpecialCompressionConfigs.CreateDamageConfigs();
        public List<StatCompressionStatConfig> specialHediffStageConfigs = SpecialCompressionConfigs.CreateHediffStageConfigs();
        public List<StatCompressionStatConfig> statConfigs = new List<StatCompressionStatConfig>();
        public List<string> activePresets = new List<string>();
        public ObjectTargetFilterSettings objectTargetFilter = new ObjectTargetFilterSettings();

        public ObjectTargetFilterSettings ObjectTargetFilter
        {
            get
            {
                EnsureObjectTargetFilter();
                return objectTargetFilter;
            }
        }

        public bool NeedsInitialSetup => settingsVersion == 0;

        public StatCompressionStatConfig BodyPartHealthConfig
        {
            get
            {
                EnsureBodyPartHealthConfig();
                return bodyPartHealthConfig;
            }
        }

        public IReadOnlyList<StatCompressionStatConfig> StatConfigs
        {
            get
            {
                EnsureStatConfigs();
                return statConfigs;
            }
        }

        public IReadOnlyList<StatCompressionStatConfig> SpecialDamageConfigs
        {
            get
            {
                EnsureSpecialDamageConfigs();
                return specialDamageConfigs;
            }
        }

        public IReadOnlyList<StatCompressionStatConfig> SpecialHediffStageConfigs
        {
            get
            {
                EnsureSpecialHediffStageConfigs();
                return specialHediffStageConfigs;
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref showInfoCardSettingsButton, "showInfoCardSettingsButton", true);
            Scribe_Values.Look(ref stage, "stage", CompressionStage.BeforePostProcessCurve);
            Scribe_Values.Look(ref autoFallbackToGlobalPostfix, "autoFallbackToGlobalPostfix", true);
            Scribe_Values.Look(ref method, "method", CompressionMethod.Logarithmic);
            Scribe_Values.Look(ref parameter, "parameter", 2f);
            Scribe_Values.Look(ref thresholdFactor, "thresholdFactor", 1f);
            Scribe_Values.Look(ref settingsVersion, "settingsVersion", 0);
            var legacyBodyPartHealthEnabled = bodyPartHealthConfig?.enabled ?? false;
            Scribe_Values.Look(ref legacyBodyPartHealthEnabled, "bodyPartHealthEnabled", false);
            Scribe_Deep.Look(ref bodyPartHealthConfig, "bodyPartHealthConfig");
            Scribe_Collections.Look(ref specialDamageConfigs, "specialDamageConfigs", LookMode.Deep);
            Scribe_Collections.Look(ref specialHediffStageConfigs, "specialHediffStageConfigs", LookMode.Deep);
            Scribe_Collections.Look(ref statConfigs, "statConfigs", LookMode.Deep);
            Scribe_Collections.Look(ref activePresets, "activePresets", LookMode.Value);
            Scribe_Deep.Look(ref objectTargetFilter, "objectTargetFilter");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureInvariantsAfterLoadOrImport(legacyBodyPartHealthEnabled);
                MigrateLoadedSettings();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StatCompressionStatConfig GetConfigFast(StatDef stat)
        {
            return configByIndex[stat.index];
        }

        internal bool TryGetConfigFast(
            StatDef stat,
            out StatCompressionStatConfig config)
        {
            var index = configByIndex;
            var statIndex = stat?.index ?? -1;
            if (index == null || statIndex < 0 || statIndex >= index.Length)
            {
                config = null;
                return false;
            }

            config = index[statIndex];
            return config != null;
        }

        public void EnsureStatConfigs()
        {
            if (initialized)
            {
                return;
            }

            EnsureInvariantsAfterLoadOrImport();

            if (statConfigs == null || statConfigs.Count == 0)
            {
                StatCompressionDefaultConfigLoader.ApplyGlobalDefaults(this);
            }

            initialized = InitializeDefaultStatConfigs(clearExisting: false);
        }

        internal void ResetToDefaultsData()
        {
            StatCompressionDefaultConfigLoader.ApplyGlobalDefaults(this);
            activePresets.Clear();
            objectTargetFilter = new ObjectTargetFilterSettings();
            settingsVersion = CurrentSettingsVersion;
            EnsureInvariantsAfterLoadOrImport();
            initialized = InitializeDefaultStatConfigs(clearExisting: true);
        }

        internal void CompleteInitialSetup()
        {
            settingsVersion = CurrentSettingsVersion;
        }

        internal void NormalizeForRuntime()
        {
            if (method == CompressionMethod.FollowGlobal)
            {
                method = CompressionMethod.Logarithmic;
            }

            parameter = NormalizeParameter(method, parameter);
            thresholdFactor = Math.Max(0.0001f, thresholdFactor);

            if (statConfigs != null)
            {
                for (var i = 0; i < statConfigs.Count; i++)
                {
                    NormalizeConfig(statConfigs[i]);
                }
            }

            EnsureBodyPartHealthConfig();
            NormalizeConfig(bodyPartHealthConfig);
            EnsureSpecialDamageConfigs();
            for (var i = 0; i < specialDamageConfigs.Count; i++)
            {
                specialDamageConfigs[i].direction = StatCompressionDirection.HigherIsBetter;
                NormalizeConfig(specialDamageConfigs[i]);
            }
            EnsureSpecialHediffStageConfigs();
            for (var i = 0; i < specialHediffStageConfigs.Count; i++)
            {
                var config = specialHediffStageConfigs[i];
                config.direction = SpecialCompressionConfigs.DirectionForHediffStage(config.defName);
                NormalizeConfig(config);
            }

        }

        internal void RebuildStatIndex()
        {
            configByIndex = BuildIndex(statConfigs);
        }

        public IEnumerable<StatCompressionStatConfig> AdvancedConfigs()
        {
            EnsureStatConfigs();
            yield return BodyPartHealthConfig;
            EnsureSpecialDamageConfigs();
            for (var i = 0; i < specialDamageConfigs.Count; i++)
            {
                yield return specialDamageConfigs[i];
            }
            EnsureSpecialHediffStageConfigs();
            for (var i = 0; i < specialHediffStageConfigs.Count; i++)
            {
                yield return specialHediffStageConfigs[i];
            }
            for (var i = 0; i < statConfigs.Count; i++)
            {
                yield return statConfigs[i];
            }
        }

        public StatCompressionStatConfig GetAdvancedConfig(string defName)
        {
            defName = SpecialCompressionConfigs.CanonicalizeId(defName);
            if (defName == SpecialCompressionConfigs.BodyPartHealthDefName)
            {
                return BodyPartHealthConfig;
            }

            EnsureSpecialDamageConfigs();
            for (var i = 0; i < specialDamageConfigs.Count; i++)
            {
                if (specialDamageConfigs[i].defName == defName)
                {
                    return specialDamageConfigs[i];
                }
            }

            EnsureSpecialHediffStageConfigs();
            for (var i = 0; i < specialHediffStageConfigs.Count; i++)
            {
                if (specialHediffStageConfigs[i].defName == defName)
                {
                    return specialHediffStageConfigs[i];
                }
            }

            return statConfigs.FirstOrDefault(config => config.defName == defName);
        }

        private void EnsureBodyPartHealthConfig()
        {
            if (bodyPartHealthConfig == null)
            {
                bodyPartHealthConfig = SpecialCompressionConfigs.CreateBodyPartHealth();
            }

            bodyPartHealthConfig.defName = SpecialCompressionConfigs.BodyPartHealthDefName;
        }

        private void EnsureObjectTargetFilter()
        {
            objectTargetFilter = objectTargetFilter ?? new ObjectTargetFilterSettings();
            objectTargetFilter.EnsureLists();
        }

        private void EnsureSpecialDamageConfigs()
        {
            if (specialDamageConfigs != null)
            {
                for (var i = 0; i < specialDamageConfigs.Count; i++)
                {
                    var config = specialDamageConfigs[i];
                    if (config != null)
                    {
                        config.defName = SpecialCompressionConfigs.CanonicalizeId(config.defName);
                    }
                }
            }

            if (specialDamageConfigs != null &&
                specialDamageConfigs.Count == SpecialCompressionConfigs.DamageDefNames.Length)
            {
                var completeAndOrdered = true;
                for (var i = 0; i < specialDamageConfigs.Count; i++)
                {
                    var config = specialDamageConfigs[i];
                    if (config == null || config.defName != SpecialCompressionConfigs.DamageDefNames[i])
                    {
                        completeAndOrdered = false;
                        break;
                    }

                    config.direction = StatCompressionDirection.HigherIsBetter;
                }

                if (completeAndOrdered)
                {
                    return;
                }
            }

            var defaults = SpecialCompressionConfigs.CreateDamageConfigs();
            var existing = (specialDamageConfigs ?? new List<StatCompressionStatConfig>())
                .Where(config => config != null && !config.defName.NullOrEmpty())
                .GroupBy(config => config.defName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            specialDamageConfigs = new List<StatCompressionStatConfig>(defaults.Count);
            for (var i = 0; i < defaults.Count; i++)
            {
                var defaultConfig = defaults[i];
                if (existing.TryGetValue(defaultConfig.defName, out var config))
                {
                    config.direction = StatCompressionDirection.HigherIsBetter;
                    specialDamageConfigs.Add(config);
                }
                else
                {
                    specialDamageConfigs.Add(defaultConfig);
                }
            }
        }

        private void EnsureSpecialHediffStageConfigs()
        {
            if (specialHediffStageConfigs != null)
            {
                for (var i = 0; i < specialHediffStageConfigs.Count; i++)
                {
                    var config = specialHediffStageConfigs[i];
                    if (config != null)
                    {
                        config.defName = SpecialCompressionConfigs.CanonicalizeId(config.defName);
                    }
                }
            }

            if (specialHediffStageConfigs != null &&
                specialHediffStageConfigs.Count == SpecialCompressionConfigs.HediffStageDefNames.Length)
            {
                var completeAndOrdered = true;
                for (var i = 0; i < specialHediffStageConfigs.Count; i++)
                {
                    var config = specialHediffStageConfigs[i];
                    if (config == null || config.defName != SpecialCompressionConfigs.HediffStageDefNames[i])
                    {
                        completeAndOrdered = false;
                        break;
                    }

                    config.direction = SpecialCompressionConfigs.DirectionForHediffStage(config.defName);
                }

                if (completeAndOrdered)
                {
                    return;
                }
            }

            var defaults = SpecialCompressionConfigs.CreateHediffStageConfigs();
            var existing = (specialHediffStageConfigs ?? new List<StatCompressionStatConfig>())
                .Where(config => config != null && !config.defName.NullOrEmpty())
                .GroupBy(config => config.defName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            specialHediffStageConfigs = new List<StatCompressionStatConfig>(defaults.Count);
            for (var i = 0; i < defaults.Count; i++)
            {
                var defaultConfig = defaults[i];
                if (existing.TryGetValue(defaultConfig.defName, out var config))
                {
                    config.direction = SpecialCompressionConfigs.DirectionForHediffStage(config.defName);
                    specialHediffStageConfigs.Add(config);
                }
                else
                {
                    specialHediffStageConfigs.Add(defaultConfig);
                }
            }
        }

    }
}

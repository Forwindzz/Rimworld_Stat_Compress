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
    public sealed class StatCompressionSettings : ModSettings
    {
        private const float DefaultMaxValue = 9999999f;

        private StatCompressionStatConfig[] configByIndex;
        private bool initialized;

        public bool enabled = true;
        public CompressionStage stage = CompressionStage.BeforePostProcessCurve;
        public bool autoFallbackToGlobalPostfix = true;
        public CompressionMethod method = CompressionMethod.Logarithmic;
        public float parameter = 2f;
        public float thresholdFactor = 1f;
        public StatCompressionStatConfig bodyPartHealthConfig = SpecialCompressionConfigs.CreateBodyPartHealth();
        public List<StatCompressionStatConfig> specialDamageConfigs = SpecialCompressionConfigs.CreateDamageConfigs();
        public List<StatCompressionStatConfig> statConfigs = new List<StatCompressionStatConfig>();

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

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref stage, "stage", CompressionStage.BeforePostProcessCurve);
            Scribe_Values.Look(ref autoFallbackToGlobalPostfix, "autoFallbackToGlobalPostfix", true);
            Scribe_Values.Look(ref method, "method", CompressionMethod.Logarithmic);
            Scribe_Values.Look(ref parameter, "parameter", 2f);
            Scribe_Values.Look(ref thresholdFactor, "thresholdFactor", 1f);
            var legacyBodyPartHealthEnabled = bodyPartHealthConfig?.enabled ?? false;
            Scribe_Values.Look(ref legacyBodyPartHealthEnabled, "bodyPartHealthEnabled", false);
            Scribe_Deep.Look(ref bodyPartHealthConfig, "bodyPartHealthConfig");
            Scribe_Collections.Look(ref specialDamageConfigs, "specialDamageConfigs", LookMode.Deep);
            Scribe_Collections.Look(ref statConfigs, "statConfigs", LookMode.Deep);

            if (bodyPartHealthConfig == null)
            {
                bodyPartHealthConfig = SpecialCompressionConfigs.CreateBodyPartHealth();
                bodyPartHealthConfig.enabled = legacyBodyPartHealthEnabled;
            }

            bodyPartHealthConfig.defName = SpecialCompressionConfigs.BodyPartHealthDefName;
            EnsureSpecialDamageConfigs();

            if (statConfigs == null)
            {
                statConfigs = new List<StatCompressionStatConfig>();
            }

            NormalizeParameters();
            RebuildLookup();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StatCompressionStatConfig GetConfigFast(StatDef stat)
        {
            return configByIndex[stat.index];
        }

        public void EnsureStatConfigs()
        {
            if (initialized)
            {
                return;
            }

            if (statConfigs == null || statConfigs.Count == 0)
            {
                StatCompressionDefaultConfigLoader.ApplyGlobalDefaults(this);
            }

            initialized = InitializeDefaultStatConfigs(clearExisting: false);
        }

        public void ResetToDefaults()
        {
            StatCompressionDefaultConfigLoader.ApplyGlobalDefaults(this);
            NormalizeParameters();
            initialized = InitializeDefaultStatConfigs(clearExisting: true);
        }

        public bool NormalizeParameters()
        {
            var oldParameter = parameter;
            var oldThresholdFactor = thresholdFactor;

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

            return Math.Abs(oldParameter - parameter) > 0.000001f ||
                   Math.Abs(oldThresholdFactor - thresholdFactor) > 0.000001f;
        }

        public void ApplyGlobalCompressionToEnabled(bool applyMethod)
        {
            NormalizeParameters();
            for (var i = 0; i < statConfigs.Count; i++)
            {
                var config = statConfigs[i];
                if (config == null || !config.enabled)
                {
                    continue;
                }

                if (applyMethod)
                {
                    config.method = method;
                }

                config.thresholdFactor = thresholdFactor;
                NormalizeConfig(config);
            }

            var bodyPartHealth = BodyPartHealthConfig;
            if (bodyPartHealth.enabled)
            {
                if (applyMethod)
                {
                    bodyPartHealth.method = method;
                }

                bodyPartHealth.thresholdFactor = thresholdFactor;
                NormalizeConfig(bodyPartHealth);
            }

            EnsureSpecialDamageConfigs();
            for (var i = 0; i < specialDamageConfigs.Count; i++)
            {
                var config = specialDamageConfigs[i];
                if (!config.enabled)
                {
                    continue;
                }

                if (applyMethod)
                {
                    config.method = method;
                }

                config.thresholdFactor = thresholdFactor;
                config.direction = StatCompressionDirection.HigherIsBetter;
                NormalizeConfig(config);
            }
        }

        public void RebuildLookup()
        {
            EnsureBodyPartHealthConfig();
            configByIndex = BuildIndex(statConfigs);
            StatCompressionRuntime.RebuildRuntimePlan(this);
            BodyPartHealthCompressionModule.NotifySettingsChanged(this);
            BaseDamageCompressionModule.NotifySettingsChanged(this);
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

        private bool InitializeDefaultStatConfigs(bool clearExisting)
        {
            var allStats = DefDatabase<StatDef>.AllDefsListForReading;
            if (allStats.NullOrEmpty())
            {
                statConfigs = statConfigs ?? new List<StatCompressionStatConfig>();
                RebuildLookup();
                return false;
            }

            var sourceConfigs = clearExisting || statConfigs == null
                ? new List<StatCompressionStatConfig>()
                : statConfigs;

            var existing = sourceConfigs
                .Where(config => config != null && !config.defName.NullOrEmpty())
                .GroupBy(config => config.defName)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var humanReq = StatRequest.For(ThingDefOf.Human, null, QualityCategory.Normal);
            var added = 0;
            var enabledByDefault = 0;
            var skippedExisting = 0;
            var skippedStaticRules = 0;
            var skippedShouldShow = 0;
            var fromDefaultPreset = 0;
            var missingDefaultPreset = 0;
            var normalizedInvalidPresetBaseline = 0;

            foreach (var stat in allStats)
            {
                if (stat == null || stat.defName.NullOrEmpty())
                {
                    continue;
                }

                if (existing.ContainsKey(stat.defName))
                {
                    skippedExisting++;
                    continue;
                }

                if (StatCompressionDefaultConfigLoader.TryGet(stat.defName, out var tableRecord))
                {
                    fromDefaultPreset++;
                    var tableEnabled = tableRecord.enabled;
                    var tableBaseline = tableRecord.baseline;
                    if (tableBaseline <= 0f)
                    {
                        tableBaseline = 1f;
                        normalizedInvalidPresetBaseline++;
                        Log.Warning(
                            $"[{StatCompressionConstants.DisplayName}] Default XML baseline for {stat.defName} " +
                            "is not positive. Using baseline=1.");
                    }

                    existing[stat.defName] = new StatCompressionStatConfig(
                        stat.defName,
                        tableEnabled,
                        tableRecord.method,
                        tableRecord.methodT,
                        tableRecord.tScale,
                        tableBaseline,
                        tableRecord.thresholdFactor,
                        tableRecord.direction);
                    added++;
                    continue;
                }

                missingDefaultPreset++;
                InitializeUnknownStat(
                    stat,
                    humanReq,
                    out var defaultEnabled,
                    ref enabledByDefault,
                    ref skippedStaticRules,
                    ref skippedShouldShow);

                Log.Warning($"[{StatCompressionConstants.DisplayName}] StatDef {stat.defName} is not in default settings XML. Using auto default: enabled={defaultEnabled}, baseline=1.");
                existing[stat.defName] = new StatCompressionStatConfig(
                    stat.defName,
                    defaultEnabled,
                    method,
                    parameter,
                    1f,
                    1f,
                    thresholdFactor,
                    StatCompressionDirection.HigherIsBetter);
                added++;
            }

            var newConfigs = existing.Values
                .OrderBy(config => config.defName, StringComparer.Ordinal)
                .ToList();
            var newIndex = BuildIndex(newConfigs);

            statConfigs = newConfigs;
            configByIndex = newIndex;
            StatCompressionRuntime.RebuildRuntimePlan(this);
            BodyPartHealthCompressionModule.NotifySettingsChanged(this);
            BaseDamageCompressionModule.NotifySettingsChanged(this);
            Log.Message($"[{StatCompressionConstants.DisplayName}] Default stat configs initialized: total={newConfigs.Count}, added={added}, fromDefaultXml={fromDefaultPreset}, missingDefaultXml={missingDefaultPreset}, xmlBaselineFallbacks={normalizedInvalidPresetBaseline}, autoEnabled={enabledByDefault}, keptExisting={skippedExisting}, skippedStaticRules={skippedStaticRules}, skippedShouldShow={skippedShouldShow}.");
            return true;
        }

        private static void InitializeUnknownStat(
            StatDef stat,
            StatRequest humanReq,
            out bool enabled,
            ref int enabledByDefault,
            ref int skippedStaticRules,
            ref int skippedShouldShow)
        {
            enabled = false;
            try
            {
                if (!PassesStaticDefaultRules(stat))
                {
                    skippedStaticRules++;
                    return;
                }

                if (!stat.Worker.ShouldShowFor(humanReq))
                {
                    skippedShouldShow++;
                    return;
                }

                enabled = true;
                enabledByDefault++;
            }
            catch (Exception ex)
            {
                Log.Message(
                    $"[{StatCompressionConstants.DisplayName}] Auto configuration disabled for unknown StatDef " +
                    $"{stat.defName}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static StatCompressionStatConfig[] BuildIndex(IEnumerable<StatCompressionStatConfig> configs)
        {
            var allStats = DefDatabase<StatDef>.AllDefsListForReading;
            var byIndex = allStats.NullOrEmpty()
                ? new StatCompressionStatConfig[0]
                : new StatCompressionStatConfig[allStats.Count];

            if (configs == null)
            {
                return byIndex;
            }

            foreach (var config in configs)
            {
                if (config == null || config.defName.NullOrEmpty())
                {
                    continue;
                }

                var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
                if (stat != null && stat.index < byIndex.Length)
                {
                    NormalizeConfig(config);
                    byIndex[stat.index] = config;
                }
            }

            return byIndex;
        }

        public string ExportSettingsToXml()
        {
            EnsureStatConfigs();
            NormalizeParameters();

            var dir = Path.Combine(GenFilePaths.ConfigFolderPath, "StatCompression");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.xml");

            StatCompressionSettingsXml.CreateDocument(this).Save(path);
            Log.Message($"[{StatCompressionConstants.DisplayName}] Exported settings XML to {path}");
            return path;
        }

        public string ImportSettingsFromXml(out int updated, out int skipped)
        {
            EnsureStatConfigs();
            var path = Path.Combine(GenFilePaths.ConfigFolderPath, "StatCompression", "settings.xml");
            updated = 0;
            skipped = 0;

            if (!File.Exists(path))
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Settings XML import file not found: {path}");
                return path;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(path);
            }
            catch (Exception ex)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Failed to read settings XML import file: {path}\n{ex}");
                return path;
            }

            if (!StatCompressionSettingsXml.TryGetRoot(document, out var root, out var rootError))
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Invalid settings XML ({rootError}): {path}");
                return path;
            }

            var importedGlobal = new DefaultGlobalSettings
            {
                enabled = enabled,
                stage = stage,
                autoFallbackToGlobalPostfix = autoFallbackToGlobalPostfix,
                method = method,
                parameter = parameter,
                thresholdFactor = thresholdFactor
            };
            StatCompressionSettingsXml.ReadGlobal(root.Element("Global"), importedGlobal);
            StatCompressionSettingsXml.ReadBodyPartHealth(root.Element("BodyPartHealth"), BodyPartHealthConfig);
            EnsureSpecialDamageConfigs();
            updated += StatCompressionSettingsXml.ReadSpecialDamageConfigs(
                root.Element("SpecialDamageConfigs"),
                specialDamageConfigs);
            enabled = importedGlobal.enabled;
            stage = importedGlobal.stage;
            autoFallbackToGlobalPostfix = importedGlobal.autoFallbackToGlobalPostfix;
            method = importedGlobal.method;
            parameter = importedGlobal.parameter;
            thresholdFactor = importedGlobal.thresholdFactor;

            var existing = statConfigs
                .Where(config => config != null && !config.defName.NullOrEmpty())
                .GroupBy(config => config.defName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var statsElement = root.Element("Stats");
            if (statsElement != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var warnedDuplicates = new HashSet<string>(StringComparer.Ordinal);
                foreach (var statElement in statsElement.Elements("Stat"))
                {
                    var defName = StatCompressionSettingsXml.Attr(statElement, "defName");
                    if (defName.NullOrEmpty())
                    {
                        skipped++;
                        continue;
                    }

                    if (!seen.Add(defName))
                    {
                        skipped++;
                        if (warnedDuplicates.Add(defName))
                        {
                            Log.Warning($"[{StatCompressionConstants.DisplayName}] Duplicate imported StatDef ignored: {defName}");
                        }

                        continue;
                    }

                    if (!existing.TryGetValue(defName, out var config))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        StatCompressionSettingsXml.ApplyStatElement(statElement, config);
                        NormalizeConfig(config);
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        Log.Warning(
                            $"[{StatCompressionConstants.DisplayName}] Skipped XML config for {defName}: " +
                            $"{ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            NormalizeParameters();
            RebuildLookup();
            Log.Message($"[{StatCompressionConstants.DisplayName}] Imported settings XML: updated={updated}, skipped={skipped}, path={path}");
            return path;
        }

        public static float NormalizeParameter(CompressionMethod method, float parameter)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return Math.Max(0f, parameter);
                case CompressionMethod.Exponential:
                    return Math.Max(0.001f, parameter);
                case CompressionMethod.Logarithmic:
                    return Math.Max(1.001f, parameter);
                case CompressionMethod.SoftCap:
                    return Math.Max(0.001f, parameter);
                default:
                    return parameter;
            }
        }

        public static void NormalizeConfig(StatCompressionStatConfig config)
        {
            if (config == null)
            {
                return;
            }

            config.method_t = NormalizeParameter(config.method, config.method_t);
            config.tScale = Math.Max(0.0001f, config.tScale);
            config.thresholdFactor = Math.Max(0.0001f, config.thresholdFactor);
        }

        public static bool PassesStaticDefaultRules(StatDef stat)
        {
            if (stat == null)
            {
                return false;
            }

            if (!stat.showOnPawns)
            {
                return false;
            }

            if (stat.postProcessCurve != null)
            {
                return false;
            }

            if (HasExplicitMaxValue(stat))
            {
                return false;
            }

            return true;
        }

        private static bool HasExplicitMaxValue(StatDef stat)
        {
            return Math.Abs(stat.maxValue - DefaultMaxValue) > 0.001f;
        }
    }
}

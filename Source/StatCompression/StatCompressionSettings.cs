using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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
        public CompressionBackend runtimeBackend = CompressionBackend.CompiledStatic;
        public bool benchmarkOnGameLoad;
        public List<StatCompressionStatConfig> statConfigs = new List<StatCompressionStatConfig>();

        public IReadOnlyList<StatCompressionStatConfig> StatConfigs
        {
            get
            {
                EnsureStatConfigs();
                return statConfigs;
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
            Scribe_Values.Look(ref runtimeBackend, "runtimeBackend", CompressionBackend.CompiledStatic);
            Scribe_Values.Look(ref benchmarkOnGameLoad, "benchmarkOnGameLoad", false);
            Scribe_Collections.Look(ref statConfigs, "statConfigs", LookMode.Deep);

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StatCompressionStatConfig GetConfigFast(int statIndex)
        {
            return configByIndex[statIndex];
        }

        public void EnsureStatConfigs()
        {
            if (initialized)
            {
                return;
            }

            initialized = InitializeDefaultStatConfigs(clearExisting: false);
        }

        public void ResetToDefaultStatConfigs()
        {
            NormalizeParameters();
            initialized = InitializeDefaultStatConfigs(clearExisting: true);
        }

        public void ResetToDefaults()
        {
            enabled = true;
            stage = CompressionStage.BeforePostProcessCurve;
            autoFallbackToGlobalPostfix = true;
            method = CompressionMethod.Logarithmic;
            parameter = 2f;
            thresholdFactor = 1f;
            runtimeBackend = CompressionBackend.CompiledStatic;
            benchmarkOnGameLoad = false;
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

            return Math.Abs(oldParameter - parameter) > 0.000001f ||
                   Math.Abs(oldThresholdFactor - thresholdFactor) > 0.000001f;
        }

        public void ApplyGlobalCompressionToEnabled()
        {
            NormalizeParameters();
            for (var i = 0; i < statConfigs.Count; i++)
            {
                var config = statConfigs[i];
                if (config == null || !config.enabled)
                {
                    continue;
                }

                config.method = method;
                config.thresholdFactor = thresholdFactor;
                NormalizeConfig(config);
            }
        }

        public void RebuildLookup(bool buildDynamicMethods = true)
        {
            configByIndex = BuildIndex(statConfigs);
            StatCompressionRuntime.RebuildRuntimePlan(this, buildDynamicMethods);
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
            var skippedInvalidBaseline = 0;
            var fromDefaultTable = 0;
            var missingDefaultTable = 0;
            var disabledInvalidTableBaseline = 0;

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
                    fromDefaultTable++;
                    var tableEnabled = tableRecord.enabled;
                    var tableBaseline = tableRecord.baseline;
                    if (tableEnabled && tableBaseline <= 0f)
                    {
                        if (!StatCompressionRuntime.TryGetHumanBaselineForConfig(stat, out tableBaseline))
                        {
                            tableEnabled = false;
                            disabledInvalidTableBaseline++;
                            Log.Warning($"[{StatCompressionConstants.DisplayName}] Default table enables {stat.defName}, but baseline is not usable and Human baseline probing failed. Disabled this stat config.");
                        }
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

                missingDefaultTable++;
                var baseline = 0f;
                var defaultEnabled = false;

                if (!PassesStaticDefaultRules(stat))
                {
                    skippedStaticRules++;
                }
                else if (!stat.Worker.ShouldShowFor(humanReq))
                {
                    skippedShouldShow++;
                }
                else if (!StatCompressionRuntime.TryGetHumanBaselineForConfig(stat, out baseline))
                {
                    skippedInvalidBaseline++;
                }
                else
                {
                    defaultEnabled = true;
                    enabledByDefault++;
                }

                Log.Warning($"[{StatCompressionConstants.DisplayName}] StatDef {stat.defName} is not in default config table. Using auto default: enabled={defaultEnabled}, baseline={baseline}.");
                existing[stat.defName] = new StatCompressionStatConfig(
                    stat.defName,
                    defaultEnabled,
                    method,
                    parameter,
                    1f,
                    baseline,
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
            StatCompressionRuntime.RebuildRuntimePlan(this, buildDynamicMethods: true);
            Log.Message($"[{StatCompressionConstants.DisplayName}] Default stat configs initialized: total={newConfigs.Count}, added={added}, fromDefaultTable={fromDefaultTable}, missingDefaultTable={missingDefaultTable}, tableDisabledInvalidBaseline={disabledInvalidTableBaseline}, autoEnabled={enabledByDefault}, keptExisting={skippedExisting}, skippedStaticRules={skippedStaticRules}, skippedShouldShow={skippedShouldShow}, skippedInvalidBaseline={skippedInvalidBaseline}.");
            return true;
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

        public string ExportStatConfigsToTsv()
        {
            EnsureStatConfigs();
            var dir = Path.Combine(GenFilePaths.ConfigFolderPath, "StatCompression");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "stat_configs.tsv");
            var humanReq = StatRequest.For(ThingDefOf.Human, null, QualityCategory.Normal);
            var builder = new StringBuilder();
            builder.AppendLine("defName\tlabel\tcategory\tenabled\tmethod\tmethod_t\ttScale\tbaseline\tthresholdFactor\tdirection\thasPostProcessCurve\thasExplicitMaxValue\tshowOnPawns\tshouldShowForHuman\tdefaultCandidate\tbaselineStatus\tworkerClass\tmodPackageId\tmodName");

            foreach (var config in StatConfigs)
            {
                var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
                var shouldShowForHuman = false;
                if (stat != null)
                {
                    try
                    {
                        shouldShowForHuman = stat.Worker.ShouldShowFor(humanReq);
                    }
                    catch
                    {
                        shouldShowForHuman = false;
                    }
                }

                var defaultCandidate = stat != null && PassesStaticDefaultRules(stat) && shouldShowForHuman;
                var baselineStatus = config.baseline > 0f
                    ? "ok"
                    : defaultCandidate
                        ? "invalid"
                        : "notCandidate";
                var mod = stat?.modContentPack;

                builder
                    .Append(Tsv(config.defName)).Append('\t')
                    .Append(Tsv(stat?.label)).Append('\t')
                    .Append(Tsv(stat?.category?.defName)).Append('\t')
                    .Append(config.enabled).Append('\t')
                    .Append(config.method).Append('\t')
                    .Append(config.method_t).Append('\t')
                    .Append(config.tScale).Append('\t')
                    .Append(config.baseline).Append('\t')
                    .Append(config.thresholdFactor).Append('\t')
                    .Append(config.direction).Append('\t')
                    .Append(stat?.postProcessCurve != null).Append('\t')
                    .Append(stat != null && HasExplicitMaxValue(stat)).Append('\t')
                    .Append(stat != null && stat.showOnPawns).Append('\t')
                    .Append(shouldShowForHuman).Append('\t')
                    .Append(defaultCandidate).Append('\t')
                    .Append(Tsv(baselineStatus)).Append('\t')
                    .Append(Tsv(stat?.Worker?.GetType().FullName)).Append('\t')
                    .Append(Tsv(mod?.PackageId)).Append('\t')
                    .Append(Tsv(mod?.Name))
                    .AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
            Log.Message($"[{StatCompressionConstants.DisplayName}] Exported stat configs to {path}");
            return path;
        }

        public string ExportSettingsToXml()
        {
            EnsureStatConfigs();
            NormalizeParameters();

            var dir = Path.Combine(GenFilePaths.ConfigFolderPath, "StatCompression");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.xml");

            var document = new XDocument(
                new XElement(
                    "StatCompressionSettings",
                    new XAttribute("version", "1"),
                    new XElement(
                        "Global",
                        new XAttribute("enabled", enabled),
                        new XAttribute("stage", stage),
                        new XAttribute("autoFallbackToGlobalPostfix", autoFallbackToGlobalPostfix),
                        new XAttribute("method", method),
                        new XAttribute("parameter", FormatFloat(parameter)),
                        new XAttribute("thresholdFactor", FormatFloat(thresholdFactor)),
                        new XAttribute("runtimeBackend", runtimeBackend),
                        new XAttribute("benchmarkOnGameLoad", benchmarkOnGameLoad)),
                    new XElement(
                        "Stats",
                        statConfigs
                            .Where(config => config != null && !config.defName.NullOrEmpty())
                            .OrderBy(config => config.defName, StringComparer.Ordinal)
                            .Select(config =>
                                new XElement(
                                    "Stat",
                                    new XAttribute("defName", config.defName),
                                    new XAttribute("enabled", config.enabled),
                                    new XAttribute("method", config.method),
                                    new XAttribute("method_t", FormatFloat(config.method_t)),
                                    new XAttribute("tScale", FormatFloat(config.tScale)),
                                    new XAttribute("baseline", FormatFloat(config.baseline)),
                                    new XAttribute("thresholdFactor", FormatFloat(config.thresholdFactor)),
                                    new XAttribute("direction", config.direction))))));

            document.Save(path);
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

            var root = document.Root;
            if (root == null || root.Name != "StatCompressionSettings")
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Invalid settings XML root: {path}");
                return path;
            }

            ImportGlobalSettings(root.Element("Global"));

            var existing = statConfigs
                .Where(config => config != null && !config.defName.NullOrEmpty())
                .ToDictionary(config => config.defName, StringComparer.Ordinal);

            var statsElement = root.Element("Stats");
            if (statsElement != null)
            {
                foreach (var statElement in statsElement.Elements("Stat"))
                {
                    var defName = Attr(statElement, "defName");
                    if (defName.NullOrEmpty() || !existing.TryGetValue(defName, out var config))
                    {
                        skipped++;
                        continue;
                    }

                    ImportStatConfig(statElement, config);
                    NormalizeConfig(config);
                    updated++;
                }
            }

            NormalizeParameters();
            RebuildLookup();
            Log.Message($"[{StatCompressionConstants.DisplayName}] Imported settings XML: updated={updated}, skipped={skipped}, path={path}");
            return path;
        }

        public string ImportStatConfigsFromTsv(out int updated, out int skipped)
        {
            EnsureStatConfigs();
            var path = Path.Combine(GenFilePaths.ConfigFolderPath, "StatCompression", "stat_configs.tsv");
            updated = 0;
            skipped = 0;

            if (!File.Exists(path))
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Stat config import file not found: {path}");
                return path;
            }

            var existing = statConfigs
                .Where(config => config != null && !config.defName.NullOrEmpty())
                .ToDictionary(config => config.defName, StringComparer.Ordinal);

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                Log.Warning($"[{StatCompressionConstants.DisplayName}] Failed to read stat config import file: {path}\n{ex}");
                return path;
            }

            if (lines.Length == 0)
            {
                return path;
            }

            var headers = BuildHeaderIndex(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].NullOrEmpty())
                {
                    continue;
                }

                var columns = lines[i].Split('\t');
                var defName = GetColumn(columns, headers, "defName");
                if (defName.NullOrEmpty() || !existing.TryGetValue(defName, out var config))
                {
                    skipped++;
                    continue;
                }

                if (bool.TryParse(GetColumn(columns, headers, "enabled"), out var enabledValue))
                {
                    config.enabled = enabledValue;
                }

                if (Enum.TryParse(GetColumn(columns, headers, "method"), out CompressionMethod methodValue))
                {
                    config.method = methodValue;
                }

                if (TryParseFloat(GetColumn(columns, headers, "method_t"), out var methodTValue))
                {
                    config.method_t = methodTValue;
                }

                if (TryParseFloat(GetColumn(columns, headers, "tScale"), out var tScaleValue))
                {
                    config.tScale = tScaleValue;
                }

                if (TryParseFloat(GetColumn(columns, headers, "baseline"), out var baselineValue))
                {
                    config.baseline = baselineValue;
                }

                if (TryParseFloat(GetColumn(columns, headers, "thresholdFactor"), out var thresholdValue))
                {
                    config.thresholdFactor = thresholdValue;
                }

                if (Enum.TryParse(GetColumn(columns, headers, "direction"), out StatCompressionDirection directionValue))
                {
                    config.direction = directionValue;
                }

                NormalizeConfig(config);
                updated++;
            }

            RebuildLookup();
            Log.Message($"[{StatCompressionConstants.DisplayName}] Imported stat configs: updated={updated}, skipped={skipped}, path={path}");
            return path;
        }

        public static float NormalizeParameter(CompressionMethod method, float parameter)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return Math.Max(0f, Math.Min(1f, parameter));
                case CompressionMethod.Exponential:
                    return Math.Max(0.001f, Math.Min(0.999f, parameter));
                case CompressionMethod.Logarithmic:
                    return Math.Max(1.001f, parameter);
                case CompressionMethod.SoftCap:
                    return Math.Max(1.001f, parameter);
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

        private static Dictionary<string, int> BuildHeaderIndex(string headerLine)
        {
            var headers = headerLine.Split('\t');
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < headers.Length; i++)
            {
                result[headers[i].Trim('\ufeff')] = i;
            }

            return result;
        }

        private static string GetColumn(string[] columns, Dictionary<string, int> headers, string name)
        {
            if (!headers.TryGetValue(name, out var index) || index < 0 || index >= columns.Length)
            {
                return string.Empty;
            }

            return columns[index];
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        private void ImportGlobalSettings(XElement element)
        {
            if (element == null)
            {
                return;
            }

            if (bool.TryParse(Attr(element, "enabled"), out var enabledValue))
            {
                enabled = enabledValue;
            }

            if (Enum.TryParse(Attr(element, "stage"), out CompressionStage stageValue))
            {
                stage = stageValue;
            }

            if (bool.TryParse(Attr(element, "autoFallbackToGlobalPostfix"), out var fallbackValue))
            {
                autoFallbackToGlobalPostfix = fallbackValue;
            }

            if (Enum.TryParse(Attr(element, "method"), out CompressionMethod methodValue))
            {
                method = methodValue;
            }

            if (TryParseFloat(Attr(element, "parameter"), out var parameterValue))
            {
                parameter = parameterValue;
            }

            if (TryParseFloat(Attr(element, "thresholdFactor"), out var thresholdValue))
            {
                thresholdFactor = thresholdValue;
            }

            if (Enum.TryParse(Attr(element, "runtimeBackend"), out CompressionBackend backendValue))
            {
                runtimeBackend = backendValue;
            }

            if (bool.TryParse(Attr(element, "benchmarkOnGameLoad"), out var benchmarkValue))
            {
                benchmarkOnGameLoad = benchmarkValue;
            }
        }

        private static void ImportStatConfig(XElement element, StatCompressionStatConfig config)
        {
            if (bool.TryParse(Attr(element, "enabled"), out var enabledValue))
            {
                config.enabled = enabledValue;
            }

            if (Enum.TryParse(Attr(element, "method"), out CompressionMethod methodValue))
            {
                config.method = methodValue;
            }

            if (TryParseFloat(Attr(element, "method_t"), out var methodTValue))
            {
                config.method_t = methodTValue;
            }

            if (TryParseFloat(Attr(element, "tScale"), out var tScaleValue))
            {
                config.tScale = tScaleValue;
            }

            if (TryParseFloat(Attr(element, "baseline"), out var baselineValue))
            {
                config.baseline = baselineValue;
            }

            if (TryParseFloat(Attr(element, "thresholdFactor"), out var thresholdValue))
            {
                config.thresholdFactor = thresholdValue;
            }

            if (Enum.TryParse(Attr(element, "direction"), out StatCompressionDirection directionValue))
            {
                config.direction = directionValue;
            }
        }

        private static string Attr(XElement element, string name)
        {
            return element.Attribute(name)?.Value ?? string.Empty;
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Tsv(string value)
        {
            return value.NullOrEmpty()
                ? string.Empty
                : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
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

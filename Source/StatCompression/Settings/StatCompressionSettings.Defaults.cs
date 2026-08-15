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
    public sealed partial class StatCompressionSettings
    {
        private bool InitializeDefaultStatConfigs(bool clearExisting)
        {
            var allStats = DefDatabase<StatDef>.AllDefsListForReading;
            if (allStats.NullOrEmpty())
            {
                statConfigs = statConfigs ?? new List<StatCompressionStatConfig>();
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
                    CompressionMethod.FollowGlobal,
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

            statConfigs = newConfigs;
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
                    byIndex[stat.index] = config;
                }
            }

            return byIndex;
        }

    }
}

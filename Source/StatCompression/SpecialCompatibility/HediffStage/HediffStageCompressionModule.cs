using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal enum HediffStageField : byte
    {
        NaturalHealingFactor,
        RegenerationRate,
        TotalBleedFactor,
        HungerRateFactor,
        RestFallFactor,
        FoodPoisoningChanceFactor
    }

    internal static class HediffStageCompressionModule
    {
        private static readonly List<FieldRecord> Records = new List<FieldRecord>();
        private static readonly Dictionary<HediffStage, List<FieldRecord>> RecordsByStage =
            new Dictionary<HediffStage, List<FieldRecord>>(ReferenceComparer<HediffStage>.Instance);

        private static bool initialized;

        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            try
            {
                ScanDefs();
                Rebuild(StatCompressionMod.Settings);
                Log.Message(
                    $"[{StatCompressionConstants.DisplayName}] HediffStage Def module initialized: " +
                    $"fields={Records.Count}, stages={RecordsByStage.Count}, changed={CountChanged()}.");
            }
            catch (Exception ex)
            {
                RestoreAll();
                Log.Error(
                    $"[{StatCompressionConstants.DisplayName}] Failed to initialize HediffStage Def compression. " +
                    $"All captured fields were restored.\n{ex}");
            }
        }

        public static void NotifySettingsChanged(StatCompressionSettings settings)
        {
            if (initialized)
            {
                Rebuild(settings);
            }
        }

        public static IEnumerable<StatDrawEntry> AppendInfoEntries(
            IEnumerable<StatDrawEntry> original,
            HediffStage stage,
            Hediff instance)
        {
            foreach (var entry in original)
            {
                yield return entry;
            }

            // A live Hediff uses TipStringExtra; keeping this entry would create a bullet summary.
            if (instance != null)
            {
                yield break;
            }

            var changed = ChangedRecords(stage);
            if (changed.Count == 0)
            {
                yield break;
            }

            if (changed.Count == 0)
            {
                yield break;
            }

            yield return new StatDrawEntry(
                StatCategoryDefOf.CapacityEffects,
                StatCompressionText.T("StatCompression_HediffStage_Info_Header"),
                StatCompressionText.T("StatCompression_HediffStage_Info_Count", changed.Count),
                BuildReport(changed),
                4010,
                null,
                null,
                false,
                false);
        }

        public static string AppendTooltipDetails(string original, HediffStage stage)
        {
            var changed = ChangedRecords(stage);
            if (changed.Count == 0)
            {
                return original;
            }

            var details = BuildReport(changed);
            return original.NullOrEmpty()
                ? details
                : original.TrimEndNewlines() + "\n" + details;
        }

        private static List<FieldRecord> ChangedRecords(HediffStage stage)
        {
            var changed = new List<FieldRecord>();
            if (stage == null || !RecordsByStage.TryGetValue(stage, out var records))
            {
                return changed;
            }

            for (var i = 0; i < records.Count; i++)
            {
                if (records[i].Changed)
                {
                    changed.Add(records[i]);
                }
            }

            return changed;
        }

        private static void Rebuild(StatCompressionSettings settings)
        {
            RestoreAll();
            if (settings == null || !settings.enabled)
            {
                return;
            }

            var compiled = new CompiledStatConfig[SpecialCompressionConfigs.HediffStageDefNames.Length];
            for (var i = 0; i < compiled.Length; i++)
            {
                compiled[i] = StatCompressionRuntimeCompiler.CompileConfig(
                    settings,
                    settings.GetAdvancedConfig(SpecialCompressionConfigs.HediffStageDefNames[i]));
            }

            for (var i = 0; i < Records.Count; i++)
            {
                var record = Records[i];
                ref var config = ref compiled[(int)record.Field];
                record.Apply(ref config);
            }
        }

        private static void ScanDefs()
        {
            Records.Clear();
            RecordsByStage.Clear();
            var defs = DefDatabase<HediffDef>.AllDefsListForReading;
            for (var defIndex = 0; defIndex < defs.Count; defIndex++)
            {
                var stages = defs[defIndex].stages;
                if (stages == null)
                {
                    continue;
                }

                for (var stageIndex = 0; stageIndex < stages.Count; stageIndex++)
                {
                    var stage = stages[stageIndex];
                    if (stage == null)
                    {
                        continue;
                    }

                    AddIfNonDefault(stage, HediffStageField.NaturalHealingFactor, stage.naturalHealingFactor, -1f);
                    AddIfNonDefault(stage, HediffStageField.RegenerationRate, stage.regeneration, -1f);
                    AddIfNonDefault(stage, HediffStageField.TotalBleedFactor, stage.totalBleedFactor, 1f);
                    AddIfNonDefault(stage, HediffStageField.HungerRateFactor, stage.hungerRateFactor, 1f);
                    AddIfNonDefault(stage, HediffStageField.RestFallFactor, stage.restFallFactor, 1f);
                    AddIfNonDefault(
                        stage,
                        HediffStageField.FoodPoisoningChanceFactor,
                        stage.foodPoisoningChanceFactor,
                        1f);
                }
            }
        }

        private static void AddIfNonDefault(
            HediffStage stage,
            HediffStageField field,
            float value,
            float defaultValue)
        {
            if (value.Equals(defaultValue))
            {
                return;
            }

            var record = new FieldRecord(stage, field, value);
            Records.Add(record);
            if (!RecordsByStage.TryGetValue(stage, out var stageRecords))
            {
                stageRecords = new List<FieldRecord>();
                RecordsByStage.Add(stage, stageRecords);
            }

            stageRecords.Add(record);
        }

        private static void RestoreAll()
        {
            for (var i = 0; i < Records.Count; i++)
            {
                Records[i].Restore();
            }
        }

        private static int CountChanged()
        {
            var count = 0;
            for (var i = 0; i < Records.Count; i++)
            {
                if (Records[i].Changed)
                {
                    count++;
                }
            }

            return count;
        }

        private static string BuildReport(List<FieldRecord> records)
        {
            var lines = new List<string>
            {
                StatCompressionText.T("StatCompression_Explanation_Separator"),
                StatCompressionText.T("StatCompression_HediffStage_Info_Header")
            };
            var settings = StatCompressionMod.Settings;
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                var config = settings.GetAdvancedConfig(record.DefName);
                var parameter = StatCompressionRuntime.GetActualParameter(
                    config.method,
                    settings.method,
                    settings.parameter,
                    config.tScale);
                lines.Add(StatCompressionText.T(
                    "StatCompression_HediffStage_Info_Value",
                    SpecialCompressionConfigs.LabelFor(record.DefName),
                    record.Format(record.Original),
                    record.Format(record.Applied)));
                lines.Add(StatCompressionText.T(
                    "StatCompression_HediffStage_Info_Method",
                    StatCompressionText.MethodLabel(config.method),
                    parameter.ToString("0.###"),
                    record.Format(config.baseline),
                    (config.thresholdFactor * 100f).ToString("0.###") + "%"));
            }

            return string.Join("\n", lines).Colorize(ColoredText.SubtleGrayColor);
        }

        private sealed class FieldRecord
        {
            public FieldRecord(HediffStage stage, HediffStageField field, float original)
            {
                Stage = stage;
                Field = field;
                Original = original;
                Applied = original;
            }

            public HediffStage Stage { get; }
            public HediffStageField Field { get; }
            public float Original { get; }
            public float Applied { get; private set; }
            public bool Changed => Math.Abs(Original - Applied) > 0.000001f;
            public string DefName => SpecialCompressionConfigs.HediffStageDefNames[(int)Field];

            public void Apply(ref CompiledStatConfig config)
            {
                Applied = StatCompressionRuntimeCompiler.ApplyStatic(ref config, Original);
                Write(Applied);
            }

            public void Restore()
            {
                Applied = Original;
                Write(Original);
            }

            public string Format(float value)
            {
                return Field == HediffStageField.RegenerationRate
                    ? StatCompressionText.T("StatCompression_HediffStage_RegenerationPerDay", value.ToString("0.###"))
                    : "x" + value.ToString("0.###");
            }

            private void Write(float value)
            {
                switch (Field)
                {
                    case HediffStageField.NaturalHealingFactor:
                        Stage.naturalHealingFactor = value;
                        break;
                    case HediffStageField.RegenerationRate:
                        Stage.regeneration = value;
                        break;
                    case HediffStageField.TotalBleedFactor:
                        Stage.totalBleedFactor = value;
                        break;
                    case HediffStageField.HungerRateFactor:
                        Stage.hungerRateFactor = value;
                        break;
                    case HediffStageField.RestFallFactor:
                        Stage.restFallFactor = value;
                        break;
                    case HediffStageField.FoodPoisoningChanceFactor:
                        Stage.foodPoisoningChanceFactor = value;
                        break;
                }
            }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

            public bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}

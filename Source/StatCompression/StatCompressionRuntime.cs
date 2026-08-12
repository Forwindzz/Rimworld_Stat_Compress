using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionRuntime
    {
        private static StatCompressionRuntimePlan activePlan =
            new StatCompressionRuntimePlan(new CompiledStatConfig[0]);

        [ThreadStatic]
        private static int suppressCompressionDepth;

        public static bool Suppressed => suppressCompressionDepth > 0;

        internal static void BeginSuppression()
        {
            suppressCompressionDepth++;
        }

        internal static void EndSuppression()
        {
            suppressCompressionDepth--;
        }

        public static void RebuildRuntimePlan(StatCompressionSettings settings)
        {
            activePlan = StatCompressionRuntimeCompiler.Compile(settings);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Compress(StatCompressionSettings settings, StatDef stat, ref float value)
        {
            var plan = activePlan;
            ref var config = ref plan.configsByIndex[stat.index];
            value = StatCompressionRuntimeCompiler.ApplyStatic(ref config, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryComputeCompressedValue(StatCompressionSettings settings, StatCompressionStatConfig config, float original, out float compressed)
        {
            var baseline = config.baseline;
            var threshold = config.thresholdFactor;
            var actualParameter = GetActualParameter(
                config.method,
                settings.method,
                settings.parameter,
                config.tScale);
            var relative = original / baseline;
            if (config.direction == StatCompressionDirection.HigherIsBetter)
            {
                if (relative <= threshold)
                {
                    compressed = original;
                    return false;
                }

                var compressedExcess = CompressExcess(relative - threshold, config.method, actualParameter);
                compressed = baseline * (threshold + compressedExcess);
                return true;
            }

            var inverseRelative = 1f / relative;
            if (inverseRelative <= threshold)
            {
                compressed = original;
                return false;
            }

            var compressedInverseExcess = CompressExcess(inverseRelative - threshold, config.method, actualParameter);
            compressed = baseline / (threshold + compressedInverseExcess);
            return true;
        }

        public static bool TryGetHumanBaselineForConfig(StatDef stat, out float baseline)
        {
            return TryGetCalculatedHumanBaseline(stat, CompressionStage.BeforePostProcessCurve, out baseline);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanRun(StatCompressionSettings settings, bool applyPostProcess)
        {
            if (!applyPostProcess || Suppressed)
            {
                return false;
            }

            return settings.enabled;
        }

        private static bool TryGetCalculatedHumanBaseline(StatDef stat, CompressionStage callStage, out float baseline)
        {
            baseline = 0f;
            if (stat == null || ThingDefOf.Human == null)
            {
                return false;
            }

            try
            {
                suppressCompressionDepth++;
                var req = StatRequest.For(ThingDefOf.Human, null, QualityCategory.Normal);
                var applyPostProcess = callStage == CompressionStage.GlobalPostfix;
                baseline = stat.Worker.GetValue(req, applyPostProcess);
                if (IsUsablePositiveNumber(baseline))
                {
                    return true;
                }

                baseline = 0f;
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                suppressCompressionDepth--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryBuildExplanation(
            StatCompressionSettings settings,
            StatDef stat,
            StatRequest req,
            ToStringNumberSense numberSense,
            float finalVal,
            out string explanation)
        {
            explanation = null;
            if (settings == null || !settings.enabled || stat == null || Suppressed)
            {
                return false;
            }

            var config = settings.GetConfigFast(stat);
            if (!config.enabled)
            {
                return false;
            }

            float original;
            try
            {
                suppressCompressionDepth++;
                original = stat.Worker.GetValue(req, true);
            }
            catch
            {
                return false;
            }
            finally
            {
                suppressCompressionDepth--;
            }

            if (Math.Abs(original - finalVal) < 0.000001f)
            {
                return false;
            }

            FormatDisplayedValuePair(stat, original, finalVal, out var originalText, out var compressedText);
            var usesRawCurveInput = settings.stage == CompressionStage.BeforePostProcessCurve &&
                                    StatWorker_FinalizeValue_Patch.BeforePostProcessPatchApplied &&
                                    stat.postProcessCurve != null;
            var baselineText = usesRawCurveInput
                ? StatCompressionText.T("StatCompression_Explanation_RawScore", config.baseline.ToString("0.###"))
                : stat.ValueToString(config.baseline, stat.toStringNumberSense, true);
            var actualParameter = GetActualParameter(
                config.method,
                settings.method,
                settings.parameter,
                config.tScale);
            var text =
                StatCompressionText.T("StatCompression_Explanation_Separator") + "\n" +
                StatCompressionText.T("StatCompression_Explanation_ValueLine", originalText, compressedText) + "\n" +
                StatCompressionText.T(
                    "StatCompression_Explanation_MethodLine",
                    StatCompressionText.MethodLabel(config.method),
                    actualParameter.ToString("0.###"),
                    baselineText);
            if (usesRawCurveInput && TryGetRawCompressionPair(settings, stat, req, config, out var rawOriginal, out var rawCompressed))
            {
                text += "\n" + StatCompressionText.T(
                    "StatCompression_Explanation_RawValueLine",
                    rawOriginal.ToString("0.###"),
                    rawCompressed.ToString("0.###"));
            }

            var hint = GetMethodHint(config.method);
            if (!hint.NullOrEmpty())
            {
                text += "\n" + hint;
            }

            explanation = text.Colorize(ColoredText.SubtleGrayColor);
            return true;
        }

        private static bool TryGetRawCompressionPair(
            StatCompressionSettings settings,
            StatDef stat,
            StatRequest req,
            StatCompressionStatConfig config,
            out float rawOriginal,
            out float rawCompressed)
        {
            rawOriginal = 0f;
            rawCompressed = 0f;
            try
            {
                suppressCompressionDepth++;
                rawOriginal = stat.Worker.GetValue(req, false);
            }
            catch
            {
                return false;
            }
            finally
            {
                suppressCompressionDepth--;
            }

            rawCompressed = TryComputeCompressedValue(settings, config, rawOriginal, out var compressed)
                ? compressed
                : rawOriginal;
            return Math.Abs(rawOriginal - rawCompressed) >= 0.000001f;
        }

        private static void FormatDisplayedValuePair(
            StatDef stat,
            float original,
            float compressed,
            out string originalText,
            out string compressedText)
        {
            originalText = stat.ValueToString(original, stat.toStringNumberSense, true);
            compressedText = stat.ValueToString(compressed, stat.toStringNumberSense, true);
            if (originalText != compressedText || Math.Abs(original - compressed) < 0.000001f)
            {
                return;
            }

            if (stat.toStringStyle == ToStringStyle.PercentZero ||
                stat.toStringStyle == ToStringStyle.PercentOne)
            {
                originalText = (original * 100f).ToString("0.###") + "%";
                compressedText = (compressed * 100f).ToString("0.###") + "%";
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetActualParameter(
            CompressionMethod method,
            CompressionMethod globalMethod,
            float globalParameter,
            float tScale)
        {
            var baseParameter = method == globalMethod
                ? globalParameter
                : DefaultParameter(method);
            if (method == CompressionMethod.Logarithmic)
            {
                return StatCompressionSettings.NormalizeParameter(method, baseParameter * tScale);
            }

            return StatCompressionSettings.NormalizeParameter(method, baseParameter / tScale);
        }

        public static float DefaultParameter(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return 0.1f;
                case CompressionMethod.Exponential:
                    return 0.5f;
                case CompressionMethod.Logarithmic:
                    return 2f;
                case CompressionMethod.SoftCap:
                    return 10f;
                default:
                    return 2f;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CompressExcess(float excess, CompressionMethod method, float parameter)
        {
            if (excess <= 0f)
            {
                return 0f;
            }

            float compressed;
            switch (method)
            {
                case CompressionMethod.Linear:
                    compressed = excess * parameter;
                    break;
                case CompressionMethod.Exponential:
                    compressed = (float)Math.Pow(excess + 1f, parameter) - 1f;
                    break;
                case CompressionMethod.Logarithmic:
                    var logarithmicStrength = Math.Log(parameter);
                    compressed = (float)(Math.Log(1d + logarithmicStrength * excess) / logarithmicStrength);
                    break;
                case CompressionMethod.SoftCap:
                    compressed = CompressSoftCapExcess(excess, parameter);
                    break;
                default:
                    compressed = excess;
                    break;
            }

            return compressed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float CompressSoftCapExcess(float excess, float parameter)
        {
            var cap = parameter;
            return cap * excess / (excess + cap);
        }

        private static string GetMethodHint(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_Explanation_Hint_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_Explanation_Hint_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_Explanation_Hint_SoftCap");
                default:
                    return string.Empty;
            }
        }

        private static bool IsUsablePositiveNumber(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}

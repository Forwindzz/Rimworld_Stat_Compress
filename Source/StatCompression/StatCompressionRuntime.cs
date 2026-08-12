using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionRuntime
    {
        [ThreadStatic]
        private static int suppressCompressionDepth;

        public static bool Suppressed => suppressCompressionDepth > 0;

        public static void ClearRuntimeCaches()
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Compress(StatCompressionSettings settings, StatDef stat, ref float value)
        {
            var config = settings.GetConfigFast(stat);
            if (!config.enabled)
            {
                return;
            }

            if (TryComputeCompressedValue(settings, config, value, out var compressed))
            {
                value = compressed;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryComputeCompressedValue(StatCompressionSettings settings, StatCompressionStatConfig config, float original, out float compressed)
        {
            var baseline = config.baseline;
            var threshold = config.thresholdFactor;
            var actualParameter = GetActualParameter(config.method, settings.parameter, config.tScale);
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

            if (!TryComputeCompressedValue(settings, config, original, out var compressed) ||
                Math.Abs(original - compressed) < 0.000001f)
            {
                return false;
            }

            var originalText = stat.ValueToString(original, stat.toStringNumberSense, true);
            var compressedText = stat.ValueToString(compressed, stat.toStringNumberSense, true);
            var baselineText = stat.ValueToString(config.baseline, stat.toStringNumberSense, true);
            var actualParameter = GetActualParameter(config.method, settings.parameter, config.tScale);
            var text =
                StatCompressionText.T("StatCompression_Explanation_Separator") + "\n" +
                StatCompressionText.T("StatCompression_Explanation_ValueLine", originalText, compressedText) + "\n" +
                StatCompressionText.T(
                    "StatCompression_Explanation_MethodLine",
                    StatCompressionText.MethodLabel(config.method),
                    actualParameter.ToString("0.###"),
                    baselineText);
            var hint = GetMethodHint(config.method);
            if (!hint.NullOrEmpty())
            {
                text += "\n" + hint;
            }

            explanation = text.Colorize(ColoredText.SubtleGrayColor);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetActualParameter(CompressionMethod method, float globalParameter, float tScale)
        {
            if (method == CompressionMethod.Logarithmic)
            {
                return StatCompressionSettings.NormalizeParameter(method, globalParameter * tScale);
            }

            return StatCompressionSettings.NormalizeParameter(method, globalParameter / tScale);
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
                    compressed = (float)(Math.Log(excess + 1f) / Math.Log(parameter));
                    break;
                case CompressionMethod.SoftCap:
                    compressed = CompressSoftCapExcess(excess, parameter);
                    break;
                default:
                    compressed = excess;
                    break;
            }

            return Math.Min(excess, compressed);
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

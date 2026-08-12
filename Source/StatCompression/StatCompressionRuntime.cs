using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionRuntime
    {
        private static CompiledStatConfig[] activeConfigsByIndex = new CompiledStatConfig[0];
        private static int runtimePlanVersion;

        [ThreadStatic]
        private static int suppressCompressionDepth;

        [ThreadStatic]
        private static ExplanationContext currentExplanation;

        [ThreadStatic]
        private static ExplanationValueCache explanationValueCache;

        public static bool Suppressed => suppressCompressionDepth > 0;

        internal sealed class ExplanationContext
        {
            public ExplanationContext parent;
            public StatDef stat;
            public StatRequest request;
            public bool rawCaptured;
            public float rawValue;
        }

        private sealed class ExplanationValueCache
        {
            public int statIndex;
            public StatRequest request;
            public float finalValue;
            public int gameTick;
            public int planVersion;
            public float uncompressedValue;
        }

        public static void RebuildRuntimePlan(StatCompressionSettings settings)
        {
            activeConfigsByIndex = StatCompressionRuntimeCompiler.Compile(settings);
            runtimePlanVersion++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Compress(StatDef stat, ref float value)
        {
            var configs = activeConfigsByIndex;
            ref var config = ref configs[stat.index];
            value = StatCompressionRuntimeCompiler.ApplyStatic(ref config, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ComputePreviewValue(
            StatCompressionSettings settings,
            StatCompressionStatConfig config,
            float original)
        {
            var compiled = StatCompressionRuntimeCompiler.CompileConfig(settings, config);
            return StatCompressionRuntimeCompiler.ApplyStatic(ref compiled, original);
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

        public static ExplanationContext BeginExplanation(
            StatCompressionSettings settings,
            StatDef stat,
            StatRequest request)
        {
            if (settings == null || !settings.enabled || stat == null || Suppressed)
            {
                return null;
            }

            var config = settings.GetConfigFast(stat);
            if (config == null || !config.enabled)
            {
                return null;
            }

            var context = new ExplanationContext
            {
                parent = currentExplanation,
                stat = stat,
                request = request
            };
            currentExplanation = context;
            return context;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CaptureExplanationRaw(
            StatDef stat,
            StatRequest request,
            bool applyPostProcess,
            float value)
        {
            var context = currentExplanation;
            if (context == null || applyPostProcess || context.rawCaptured || context.stat != stat ||
                !context.request.Equals(request))
            {
                return;
            }

            context.rawValue = value;
            context.rawCaptured = true;
        }

        public static void EndExplanation(ExplanationContext context)
        {
            if (context != null && currentExplanation == context)
            {
                currentExplanation = context.parent;
            }
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
            float finalVal,
            ExplanationContext context,
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

            if (!TryGetUncompressedFinal(stat, req, finalVal, out var original))
            {
                return false;
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
            if (usesRawCurveInput && context != null && context.rawCaptured)
            {
                var rawOriginal = context.rawValue;
                var rawCompressed = ComputePreviewValue(settings, config, rawOriginal);
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

        private static bool TryGetUncompressedFinal(
            StatDef stat,
            StatRequest req,
            float finalValue,
            out float uncompressedValue)
        {
            var gameTick = Find.TickManager?.TicksGame ?? -1;
            var cache = explanationValueCache;
            if (cache != null &&
                cache.statIndex == stat.index &&
                cache.request.Equals(req) &&
                cache.finalValue.Equals(finalValue) &&
                cache.gameTick == gameTick &&
                cache.planVersion == runtimePlanVersion)
            {
                uncompressedValue = cache.uncompressedValue;
                return true;
            }

            try
            {
                suppressCompressionDepth++;
                uncompressedValue = stat.Worker.GetValue(req, true);
            }
            catch
            {
                uncompressedValue = 0f;
                return false;
            }
            finally
            {
                suppressCompressionDepth--;
            }

            explanationValueCache = new ExplanationValueCache
            {
                statIndex = stat.index,
                request = req,
                finalValue = finalValue,
                gameTick = gameTick,
                planVersion = runtimePlanVersion,
                uncompressedValue = uncompressedValue
            };
            return true;
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

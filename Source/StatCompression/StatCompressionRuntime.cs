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

        private static ExplanationContext currentExplanation;

        private static ExplanationValueCache explanationValueCache;

        internal sealed class ExplanationContext
        {
            public ExplanationContext parent;
            public StatDef stat;
            public StatRequest request;
            public StatCompressionStatConfig config;
            public bool captureCompressionInput;
            public bool compressionInputCaptured;
            public float compressionInput;
            public float compressionOutput;
            public CompiledStatConfig captureConfig;
        }

        private sealed class ExplanationValueCache
        {
            public int statIndex;
            public StatRequest request;
            public float finalValue;
            public int gameTick;
            public int planVersion;
            public float uncompressedValue;
            public bool compressionInputCaptured;
            public float compressionInput;
            public float compressionOutput;
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
        public static void CompressBeforePostProcess(StatDef stat, StatRequest request, ref float value)
        {
            var context = currentExplanation;
            if (context != null && context.captureCompressionInput && !context.compressionInputCaptured &&
                context.stat == stat && context.request.Equals(request))
            {
                context.compressionInput = value;
                var captureConfig = context.captureConfig;
                context.compressionOutput = StatCompressionRuntimeCompiler.ApplyStatic(ref captureConfig, value);
                context.compressionInputCaptured = true;
            }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanRun(StatCompressionSettings settings, bool applyPostProcess)
        {
            return applyPostProcess && settings.enabled;
        }

        public static ExplanationContext BeginExplanation(
            StatDef stat,
            StatRequest request)
        {
            var settings = StatCompressionMod.Settings;
            if (!settings.enabled)
            {
                return null;
            }

            var config = settings.GetConfigFast(stat);
            if (!config.enabled)
            {
                return null;
            }

            var context = new ExplanationContext
            {
                parent = currentExplanation,
                stat = stat,
                request = request,
                config = config
            };
            currentExplanation = context;
            return context;
        }

        public static void EndExplanation(ExplanationContext context)
        {
            currentExplanation = context.parent;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryBuildExplanation(
            ExplanationContext context,
            float finalVal,
            out string explanation)
        {
            explanation = null;
            var settings = StatCompressionMod.Settings;
            var stat = context.stat;
            var config = context.config;

            if (!TryGetUncompressedFinal(context, finalVal, out var original))
            {
                return false;
            }

            if (Math.Abs(original - finalVal) < 0.000001f)
            {
                return false;
            }

            var usesRawCurveInput = StatCompressionBootstrap.ActiveStage == CompressionStage.BeforePostProcessCurve &&
                                    stat.postProcessCurve != null;
            var displayedOriginal = original;
            var displayedCompressed = finalVal;
            if (!usesRawCurveInput && context.compressionInputCaptured)
            {
                displayedOriginal = context.compressionInput;
                displayedCompressed = context.compressionOutput;
            }

            FormatDisplayedValuePair(
                stat,
                displayedOriginal,
                displayedCompressed,
                out var originalText,
                out var compressedText);
            var baselineText = usesRawCurveInput
                ? StatCompressionText.T("StatCompression_Explanation_RawScore", config.baseline.ToString("0.###"))
                : stat.ValueToString(config.baseline, stat.toStringNumberSense, true);
            var actualParameter = GetActualParameter(
                config.method,
                settings.method,
                settings.parameter,
                config.tScale);
            var actualMethod = ResolveMethod(config.method, settings.method);
            var text =
                StatCompressionText.T("StatCompression_Explanation_Separator") + "\n" +
                StatCompressionText.T("StatCompression_Explanation_ValueLine", originalText, compressedText) + "\n" +
                StatCompressionText.T(
                    "StatCompression_Explanation_MethodLine",
                    StatCompressionText.MethodLabel(actualMethod),
                    actualParameter.ToString("0.###"),
                    baselineText);
            text += "\n" + StatCompressionText.T(
                "StatCompression_Explanation_DirectionLine",
                StatCompressionText.DirectionExplanation(config.direction));
            if (usesRawCurveInput && context.compressionInputCaptured)
            {
                text += "\n" + StatCompressionText.T(
                    "StatCompression_Explanation_RawValueLine",
                    context.compressionInput.ToString("0.###"),
                    context.compressionOutput.ToString("0.###"));
            }

            var hint = GetMethodHint(actualMethod, config.direction);
            if (!hint.NullOrEmpty())
            {
                text += "\n" + hint;
            }

            explanation = text.Colorize(ColoredText.SubtleGrayColor);
            return true;
        }

        private static bool TryGetUncompressedFinal(
            ExplanationContext context,
            float finalValue,
            out float uncompressedValue)
        {
            var stat = context.stat;
            var req = context.request;
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
                context.compressionInputCaptured = cache.compressionInputCaptured;
                context.compressionInput = cache.compressionInput;
                context.compressionOutput = cache.compressionOutput;
                return true;
            }

            var configs = activeConfigsByIndex;
            ref var config = ref configs[stat.index];
            var previousConfig = config;
            try
            {
                context.captureConfig = previousConfig;
                context.captureCompressionInput =
                    StatCompressionBootstrap.ActiveStage == CompressionStage.BeforePostProcessCurve;
                context.compressionInputCaptured = false;
                config.kernel = CompressionKernel.Disabled;
                uncompressedValue = stat.Worker.GetValue(req, true);
            }
            catch
            {
                uncompressedValue = 0f;
                return false;
            }
            finally
            {
                context.captureCompressionInput = false;
                config = previousConfig;
            }

            explanationValueCache = new ExplanationValueCache
            {
                statIndex = stat.index,
                request = req,
                finalValue = finalValue,
                gameTick = gameTick,
                planVersion = runtimePlanVersion,
                uncompressedValue = uncompressedValue,
                compressionInputCaptured = context.compressionInputCaptured,
                compressionInput = context.compressionInput,
                compressionOutput = context.compressionOutput
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
            var actualMethod = ResolveMethod(method, globalMethod);
            var baseParameter = method == CompressionMethod.FollowGlobal
                ? globalParameter
                : DefaultParameter(actualMethod);
            if (actualMethod == CompressionMethod.Logarithmic)
            {
                return StatCompressionSettings.NormalizeParameter(actualMethod, baseParameter * tScale);
            }

            return StatCompressionSettings.NormalizeParameter(actualMethod, baseParameter / tScale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CompressionMethod ResolveMethod(
            CompressionMethod method,
            CompressionMethod globalMethod)
        {
            return method == CompressionMethod.FollowGlobal ? globalMethod : method;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetActualThresholdFactor(
            CompressionMethod method,
            float globalThresholdFactor,
            float configThresholdFactor)
        {
            return method == CompressionMethod.FollowGlobal
                ? globalThresholdFactor
                : configThresholdFactor;
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

        private static string GetMethodHint(
            CompressionMethod method,
            StatCompressionDirection direction)
        {
            if (direction == StatCompressionDirection.LowerDirect)
            {
                switch (method)
                {
                    case CompressionMethod.Exponential:
                        return StatCompressionText.T("StatCompression_Explanation_Hint_Power_LowerDirect");
                    case CompressionMethod.Logarithmic:
                        return StatCompressionText.T("StatCompression_Explanation_Hint_Logarithmic_LowerDirect");
                    case CompressionMethod.SoftCap:
                        return StatCompressionText.T("StatCompression_Explanation_Hint_SoftCap_Lower");
                }
            }

            if (direction == StatCompressionDirection.LowerIsBetter &&
                method == CompressionMethod.SoftCap)
            {
                return StatCompressionText.T("StatCompression_Explanation_Hint_SoftCap_Lower");
            }

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

    }
}

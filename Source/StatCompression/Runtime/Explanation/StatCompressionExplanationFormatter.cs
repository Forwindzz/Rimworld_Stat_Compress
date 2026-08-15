using System;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionExplanationFormatter
    {
        public static string Build(
            StatCompressionSettings settings,
            StatDef stat,
            StatCompressionStatConfig config,
            float original,
            float finalValue,
            bool compressionInputCaptured,
            float compressionInput,
            float compressionOutput)
        {
            var usesRawCurveInput =
                StatCompressionBootstrap.ActiveStage == CompressionStage.BeforePostProcessCurve &&
                stat.postProcessCurve != null;
            var displayedOriginal = original;
            var displayedCompressed = finalValue;
            if (!usesRawCurveInput && compressionInputCaptured)
            {
                displayedOriginal = compressionInput;
                displayedCompressed = compressionOutput;
            }

            FormatDisplayedValuePair(
                stat,
                displayedOriginal,
                displayedCompressed,
                out var originalText,
                out var compressedText);
            var baselineText = usesRawCurveInput
                ? StatCompressionText.T(
                    "StatCompression_Explanation_RawScore",
                    config.baseline.ToString("0.###"))
                : stat.ValueToString(config.baseline, stat.toStringNumberSense, true);
            var compiled = StatCompressionRuntimeCompiler.CompileConfig(settings, config);
            var thresholdText = usesRawCurveInput
                ? StatCompressionText.T(
                    "StatCompression_Explanation_RawScore",
                    compiled.thresholdValue.ToString("0.###"))
                : stat.ValueToString(
                    compiled.thresholdValue,
                    stat.toStringNumberSense,
                    true);
            var actualParameter = StatCompressionRuntime.GetActualParameter(
                config.method,
                settings.method,
                settings.parameter,
                config.tScale);
            var actualMethod = StatCompressionRuntime.ResolveMethod(config.method, settings.method);
            var text =
                StatCompressionText.T("StatCompression_Explanation_Separator") + "\n" +
                StatCompressionText.T(
                    "StatCompression_Explanation_ValueLine",
                    originalText,
                    compressedText) + "\n" +
                StatCompressionText.T(
                    "StatCompression_Explanation_MethodLine",
                    StatCompressionText.MethodLabel(actualMethod),
                    actualParameter.ToString("0.###"),
                    baselineText);
            text += "\n" + StatCompressionText.T(
                "StatCompression_Explanation_ThresholdLine",
                thresholdText,
                (config.thresholdFactor * 100f).ToString("0.###") + "%");
            text += "\n" + StatCompressionText.T(
                "StatCompression_Explanation_DirectionLine",
                StatCompressionText.DirectionExplanation(config.direction));
            if (usesRawCurveInput && compressionInputCaptured)
            {
                text += "\n" + StatCompressionText.T(
                    "StatCompression_Explanation_RawValueLine",
                    compressionInput.ToString("0.###"),
                    compressionOutput.ToString("0.###"));
            }

            var hint = GetMethodHint(actualMethod, config.direction);
            if (!hint.NullOrEmpty())
            {
                text += "\n" + hint;
            }

            return text.Colorize(ColoredText.SubtleGrayColor);
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

        private static string GetMethodHint(
            CompressionMethod method,
            StatCompressionDirection direction)
        {
            if (direction == StatCompressionDirection.LowerDirect)
            {
                switch (method)
                {
                    case CompressionMethod.Exponential:
                        return StatCompressionText.T(
                            "StatCompression_Explanation_Hint_Power_LowerDirect");
                    case CompressionMethod.Logarithmic:
                        return StatCompressionText.T(
                            "StatCompression_Explanation_Hint_Logarithmic_LowerDirect");
                    case CompressionMethod.SoftCap:
                        return StatCompressionText.T(
                            "StatCompression_Explanation_Hint_SoftCap_Lower");
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

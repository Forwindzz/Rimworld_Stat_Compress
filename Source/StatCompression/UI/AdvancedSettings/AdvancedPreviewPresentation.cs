using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed partial class AdvancedPreviewComponent
    {
        private void SelectPreviewValues(
            StatCompressionDirection direction,
            out float[] values,
            out string[] buffers)
        {
            if (direction == StatCompressionDirection.HigherIsBetter)
            {
                values = higherPreviewPercents;
                buffers = higherPreviewBuffers;
            }
            else if (direction == StatCompressionDirection.LowerDirect)
            {
                values = lowerDirectPreviewPercents;
                buffers = lowerDirectPreviewBuffers;
            }
            else
            {
                values = lowerPreviewPercents;
                buffers = lowerPreviewBuffers;
            }
        }

        private static void DrawInputGridLine(
            Rect plot,
            float fraction,
            string label,
            Color color)
        {
            var x = Mathf.Lerp(plot.x, plot.xMax, fraction);
            Widgets.DrawLine(
                new Vector2(x, plot.y),
                new Vector2(x, plot.yMax),
                color,
                1f);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(x - 34f, plot.yMax + 1f, 68f, 18f), label);
        }

        private static float TransformInputAxis(float value, bool signed)
        {
            return signed
                ? Math.Sign(value) * (float)Math.Log10(1f + Math.Abs(value) / 100f)
                : (float)Math.Log10(Math.Max(0.000001f, value));
        }

        private static float InverseInputAxis(float value, bool signed)
        {
            return signed
                ? Math.Sign(value) * 100f * ((float)Math.Pow(10d, Math.Abs(value)) - 1f)
                : (float)Math.Pow(10d, value);
        }

        private static float PreviewMappedPercent(
            StatCompressionStatConfig config,
            ref CompiledStatConfig compiled,
            float inputPercent)
        {
            var original = config.baseline * inputPercent / 100f;
            var mapped = StatCompressionRuntimeCompiler.ApplyStatic(ref compiled, original);
            return mapped / config.baseline * 100f;
        }

        private static string BuildDetails(
            StatCompressionStatConfig config,
            StatDef stat,
            GlobalCompressionInput global,
            ref CompiledStatConfig compiled,
            CompressionMethod actualMethod,
            float actualParameter)
        {
            var baseline = FormatValuePair(stat, config.baseline);
            var trigger = FormatValuePair(stat, compiled.thresholdValue);
            var triggerOperator = config.direction == StatCompressionDirection.LowerIsBetter
                ? "÷"
                : "×";
            var triggerText = StatCompressionText.T(
                "StatCompression_AdvancedDetail_Trigger",
                baseline,
                (config.thresholdFactor * 100f).ToString("0.###") + "%",
                triggerOperator,
                trigger);

            var selectedMethod = config.method == CompressionMethod.FollowGlobal
                ? StatCompressionText.T(
                    "StatCompression_AdvancedDetail_FollowGlobalMethod",
                    StatCompressionText.MethodLabel(actualMethod))
                : StatCompressionText.MethodLabel(actualMethod);
            var methodText = StatCompressionText.T(
                "StatCompression_AdvancedDetail_Method",
                selectedMethod,
                config.tScale.ToString("0.###"),
                actualParameter.ToString("0.###"),
                ParameterMeaning(actualMethod),
                CompressionExpression(actualMethod, actualParameter),
                MethodDescription(actualMethod));

            string directionKey;
            switch (config.direction)
            {
                case StatCompressionDirection.HigherIsBetter:
                    directionKey = "StatCompression_AdvancedDetail_DirectionHigher";
                    break;
                case StatCompressionDirection.LowerDirect:
                    directionKey = "StatCompression_AdvancedDetail_DirectionLowerDirect";
                    break;
                default:
                    directionKey = "StatCompression_AdvancedDetail_DirectionLower";
                    break;
            }

            return triggerText +
                   "\n\n" + methodText +
                   "\n\n" + StatCompressionText.T(directionKey) +
                   "\n\n" + StatCompressionText.T("StatCompression_AdvancedDetail_Flow");
        }

        private static string CompressionExpression(
            CompressionMethod method,
            float parameter)
        {
            var t = parameter.ToString("0.###");
            switch (method)
            {
                case CompressionMethod.Linear:
                    return "F(e) = e × " + t;
                case CompressionMethod.Exponential:
                    return "F(e) = (e + 1)^" + t + " - 1";
                case CompressionMethod.Logarithmic:
                    return "F(e) = ln(1 + ln(" + t + ") × e) ÷ ln(" + t + ")";
                case CompressionMethod.SoftCap:
                    return "F(e) = " + t + " × e ÷ (e + " + t + ")";
                default:
                    return "F(e) = e";
            }
        }

        private static string ParameterMeaning(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_SoftCap");
                default:
                    return string.Empty;
            }
        }

        private static string MethodDescription(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_MethodDescription_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_MethodDescription_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_MethodDescription_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_MethodDescription_SoftCap");
                default:
                    return string.Empty;
            }
        }

        private static string FormatValuePair(StatDef stat, float value)
        {
            var raw = value.ToString("0.###");
            var display = FormatStatValue(stat, value);
            return display == raw ? raw : raw + " (" + display + ")";
        }

        private static string FormatStatValue(StatDef stat, float value)
        {
            return stat == null
                ? value.ToString("0.###")
                : stat.ValueToString(value, stat.toStringNumberSense, true);
        }

        private static string FormatPreviewPercent(float value)
        {
            return value.ToString("0.###") + "%";
        }

        private static string FormatAxisPercent(float value)
        {
            if (value >= 1000000f)
            {
                return value.ToString("0.##E+0") + "%";
            }

            return value.ToString(value < 1f ? "0.###" : "0.##") + "%";
        }

        private static string LabelFor(StatCompressionStatConfig config, StatDef stat)
        {
            return SpecialCompressionConfigs.IsSpecial(config.defName)
                ? SpecialCompressionConfigs.LabelFor(config.defName)
                : stat?.LabelCap.ToString() ??
                  StatCompressionText.T("StatCompression_MissingStat_Label");
        }
    }
}

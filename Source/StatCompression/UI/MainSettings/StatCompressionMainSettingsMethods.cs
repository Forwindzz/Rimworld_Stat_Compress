using UnityEngine;
using Verse;

namespace StatCompression
{
    internal static partial class StatCompressionMainSettingsPanel
    {
        private static FloatRange ParameterRange(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return new FloatRange(0f, 1f);
                case CompressionMethod.Exponential:
                    return new FloatRange(0.001f, 0.999f);
                case CompressionMethod.Logarithmic:
                    return new FloatRange(1.001f, 10f);
                case CompressionMethod.SoftCap:
                    return new FloatRange(1.001f, 100f);
                default:
                    return new FloatRange(1.001f, 10f);
            }
        }

        private static float SliderRoundTo(CompressionMethod method)
        {
            return method == CompressionMethod.SoftCap ? 0.5f : 0.01f;
        }

        private static float ParameterSafetyMinimum(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return 0f;
                case CompressionMethod.Exponential:
                    return 0.001f;
                case CompressionMethod.Logarithmic:
                    return 1.001f;
                case CompressionMethod.SoftCap:
                    return 0.001f;
                default:
                    return 0.001f;
            }
        }

        private static string FormulaText(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_Formula_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_Formula_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_Formula_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_Formula_SoftCap");
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

        private static string ParameterLabel(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_ParameterLabel_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_ParameterLabel_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_ParameterLabel_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_ParameterLabel_SoftCap");
                default:
                    return StatCompressionText.T("StatCompression_ParameterT");
            }
        }

        private static string ParameterDirectionDescription(CompressionMethod method)
        {
            return StatCompressionText.T(
                method == CompressionMethod.Logarithmic
                    ? "StatCompression_ParameterDirection_Larger"
                    : "StatCompression_ParameterDirection_Smaller");
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

        private static string ParameterTooltip(CompressionMethod method)
        {
            return StatCompressionText.T("StatCompression_ParameterTooltip") +
                   "\n" +
                   ParameterDirectionDescription(method) +
                   "\n" +
                   FormulaText(method);
        }

    }
}

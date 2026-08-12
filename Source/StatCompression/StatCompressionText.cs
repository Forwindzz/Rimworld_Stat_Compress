using Verse;

namespace StatCompression
{
    internal static class StatCompressionText
    {
        public static string T(string key)
        {
            return key.Translate().ToString();
        }

        public static string T(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        public static string MethodLabel(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return T("StatCompression_Method_Linear");
                case CompressionMethod.Exponential:
                    return T("StatCompression_Method_Power");
                case CompressionMethod.Logarithmic:
                    return T("StatCompression_Method_Logarithmic");
                case CompressionMethod.SoftCap:
                    return T("StatCompression_Method_SoftCap");
                default:
                    return method.ToString();
            }
        }

        public static string MethodShortLabel(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Logarithmic:
                    return T("StatCompression_Method_Log");
                default:
                    return MethodLabel(method);
            }
        }

        public static string DirectionShortLabel(StatCompressionDirection direction)
        {
            switch (direction)
            {
                case StatCompressionDirection.HigherIsBetter:
                    return T("StatCompression_Direction_Higher");
                case StatCompressionDirection.LowerIsBetter:
                    return T("StatCompression_Direction_Lower");
                case StatCompressionDirection.LowerDirect:
                    return T("StatCompression_Direction_LowerDirect");
                default:
                    return direction.ToString();
            }
        }

        public static string DirectionExplanation(StatCompressionDirection direction)
        {
            switch (direction)
            {
                case StatCompressionDirection.HigherIsBetter:
                    return T("StatCompression_DirectionExplanation_Higher");
                case StatCompressionDirection.LowerIsBetter:
                    return T("StatCompression_DirectionExplanation_Lower");
                case StatCompressionDirection.LowerDirect:
                    return T("StatCompression_DirectionExplanation_LowerDirect");
                default:
                    return direction.ToString();
            }
        }

    }
}

using System;

namespace StatCompression
{
    internal sealed class SimplePreviewCache
    {
        private static readonly float[] HigherValues =
            { 0.5f, 1f, 1.5f, 2f, 5f, 50f, 1000f };

        private static readonly float[] LowerValues =
            { 1.5f, 1f, 0.75f, 0.4f, 0.1f, 0.01f, 0.001f };

        private readonly string[] higherInputs = FormatInputs(HigherValues);
        private readonly string[] higherOutputs = new string[HigherValues.Length];
        private readonly string[] lowerInputs = FormatInputs(LowerValues);
        private readonly string[] lowerOutputs = new string[LowerValues.Length];

        private bool initialized;
        private CompressionMethod method;
        private float parameter;
        private float thresholdFactor;

        public string[] HigherInputs => higherInputs;
        public string[] HigherOutputs => higherOutputs;
        public string[] LowerInputs => lowerInputs;
        public string[] LowerOutputs => lowerOutputs;

        public void SetInput(StatCompressionSettings settings)
        {
            if (initialized &&
                method == settings.method &&
                parameter.Equals(settings.parameter) &&
                thresholdFactor.Equals(settings.thresholdFactor))
            {
                return;
            }

            initialized = true;
            method = settings.method;
            parameter = settings.parameter;
            thresholdFactor = settings.thresholdFactor;

            var higher = Compile(settings, StatCompressionDirection.HigherIsBetter);
            var lower = Compile(settings, StatCompressionDirection.LowerIsBetter);
            for (var i = 0; i < HigherValues.Length; i++)
            {
                higherOutputs[i] = FormatPercent(
                    StatCompressionRuntimeCompiler.ApplyStatic(ref higher, HigherValues[i]));
            }

            for (var i = 0; i < LowerValues.Length; i++)
            {
                lowerOutputs[i] = FormatPercent(
                    StatCompressionRuntimeCompiler.ApplyStatic(ref lower, LowerValues[i]));
            }
        }

        private static CompiledStatConfig Compile(
            StatCompressionSettings settings,
            StatCompressionDirection direction)
        {
            var config = new StatCompressionStatConfig(
                "SimplePreview",
                true,
                CompressionMethod.FollowGlobal,
                settings.parameter,
                1f,
                1f,
                settings.thresholdFactor,
                direction);
            return StatCompressionRuntimeCompiler.CompileConfig(settings, config);
        }

        private static string[] FormatInputs(float[] values)
        {
            var result = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                result[i] = FormatPercent(values[i]);
            }

            return result;
        }

        private static string FormatPercent(float value)
        {
            return (value * 100f).ToString("0.###") + "%";
        }
    }
}

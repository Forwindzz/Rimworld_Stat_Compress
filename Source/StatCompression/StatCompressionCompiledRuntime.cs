using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal enum CompressionKernel : byte
    {
        Disabled,
        HigherLinear,
        HigherPower,
        HigherLogarithmic,
        HigherSoftCap,
        LowerLinear,
        LowerPower,
        LowerLogarithmic,
        LowerSoftCap,
        LowerDirectLinear,
        LowerDirectPower,
        LowerDirectLogarithmic,
        LowerDirectSoftCap
    }

    internal struct CompiledStatConfig
    {
        public CompressionKernel kernel;
        public float thresholdValue;
        public float thresholdFactor;
        public float baseline;
        public float invBaseline;
        public float parameter0;
        public float parameter1;
    }

    internal static class StatCompressionRuntimeCompiler
    {
        public static CompiledStatConfig[] Compile(StatCompressionSettings settings)
        {
            var allStats = DefDatabase<StatDef>.AllDefsListForReading;
            var count = allStats.NullOrEmpty() ? 0 : allStats.Count;
            var compiled = new CompiledStatConfig[count];

            for (var i = 0; i < count; i++)
            {
                var stat = allStats[i];
                var config = settings.GetConfigFast(stat);
                compiled[stat.index] = CompileConfig(settings, config);
            }

            return compiled;
        }

        internal static CompiledStatConfig CompileConfig(
            StatCompressionSettings settings,
            StatCompressionStatConfig config)
        {
            if (config == null || !config.enabled)
            {
                return new CompiledStatConfig { kernel = CompressionKernel.Disabled };
            }

            var baseline = config.baseline;
            var threshold = config.thresholdFactor;
            var actualParameter = StatCompressionRuntime.GetActualParameter(
                config.method,
                settings.method,
                settings.parameter,
                config.tScale);
            var direction = config.direction;
            var direct = direction != StatCompressionDirection.LowerIsBetter;
            var result = new CompiledStatConfig
            {
                kernel = KernelFor(config.method, direction),
                thresholdValue = direct ? baseline * threshold : baseline / threshold,
                thresholdFactor = threshold,
                baseline = baseline,
                invBaseline = 1f / baseline
            };

            switch (config.method)
            {
                case CompressionMethod.Linear:
                    result.parameter0 = actualParameter;
                    break;
                case CompressionMethod.Exponential:
                    result.parameter0 = direct ? result.invBaseline : actualParameter;
                    result.parameter1 = actualParameter;
                    break;
                case CompressionMethod.Logarithmic:
                    var logarithmicStrength = (float)Math.Log(actualParameter);
                    result.parameter0 = direct
                        ? logarithmicStrength * result.invBaseline
                        : logarithmicStrength;
                    result.parameter1 = direct
                        ? baseline / logarithmicStrength
                        : 1f / logarithmicStrength;
                    break;
                case CompressionMethod.SoftCap:
                    result.parameter0 = direct ? baseline * actualParameter : actualParameter;
                    break;
            }

            return result;
        }

        private static CompressionKernel KernelFor(
            CompressionMethod method,
            StatCompressionDirection direction)
        {
            if (direction == StatCompressionDirection.HigherIsBetter)
            {
                switch (method)
                {
                    case CompressionMethod.Linear:
                        return CompressionKernel.HigherLinear;
                    case CompressionMethod.Exponential:
                        return CompressionKernel.HigherPower;
                    case CompressionMethod.Logarithmic:
                        return CompressionKernel.HigherLogarithmic;
                    case CompressionMethod.SoftCap:
                        return CompressionKernel.HigherSoftCap;
                }
            }
            if (direction == StatCompressionDirection.LowerIsBetter)
            {
                switch (method)
                {
                    case CompressionMethod.Linear:
                        return CompressionKernel.LowerLinear;
                    case CompressionMethod.Exponential:
                        return CompressionKernel.LowerPower;
                    case CompressionMethod.Logarithmic:
                        return CompressionKernel.LowerLogarithmic;
                    case CompressionMethod.SoftCap:
                        return CompressionKernel.LowerSoftCap;
                }
            }

            if (direction == StatCompressionDirection.LowerDirect)
            {
                switch (method)
                {
                    case CompressionMethod.Linear:
                        return CompressionKernel.LowerDirectLinear;
                    case CompressionMethod.Exponential:
                        return CompressionKernel.LowerDirectPower;
                    case CompressionMethod.Logarithmic:
                        return CompressionKernel.LowerDirectLogarithmic;
                    case CompressionMethod.SoftCap:
                        return CompressionKernel.LowerDirectSoftCap;
                }
            }

            return CompressionKernel.Disabled;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldCompress(ref CompiledStatConfig config, float value)
        {
            if (config.kernel == CompressionKernel.Disabled)
            {
                return false;
            }

            if (config.kernel <= CompressionKernel.HigherSoftCap)
            {
                return !(value <= config.thresholdValue);
            }

            if (config.kernel <= CompressionKernel.LowerSoftCap)
            {
                if (float.IsNaN(value))
                {
                    return true;
                }

                return value >= 0f && value < config.thresholdValue;
            }

            return !(value >= config.thresholdValue);
        }

        public static float ApplyStatic(ref CompiledStatConfig config, float value)
        {
            if (!ShouldCompress(ref config, value))
            {
                return value;
            }

            switch (config.kernel)
            {
                case CompressionKernel.HigherLinear:
                    return config.thresholdValue + (value - config.thresholdValue) * config.parameter0;
                case CompressionKernel.HigherPower:
                    return config.thresholdValue + config.baseline *
                        ((float)Math.Pow(1f + (value - config.thresholdValue) * config.parameter0, config.parameter1) - 1f);
                case CompressionKernel.HigherLogarithmic:
                    return config.thresholdValue + config.parameter1 *
                        (float)Math.Log(1f + config.parameter0 * (value - config.thresholdValue));
                case CompressionKernel.HigherSoftCap:
                    var higherDelta = value - config.thresholdValue;
                    return config.thresholdValue + config.parameter0 * higherDelta / (higherDelta + config.parameter0);
                case CompressionKernel.LowerLinear:
                case CompressionKernel.LowerPower:
                case CompressionKernel.LowerLogarithmic:
                case CompressionKernel.LowerSoftCap:
                    return ApplyStaticLower(ref config, value);
                case CompressionKernel.LowerDirectLinear:
                    return config.thresholdValue -
                        (config.thresholdValue - value) * config.parameter0;
                case CompressionKernel.LowerDirectPower:
                    return config.thresholdValue - config.baseline *
                        ((float)Math.Pow(
                            1f + (config.thresholdValue - value) * config.parameter0,
                            config.parameter1) - 1f);
                case CompressionKernel.LowerDirectLogarithmic:
                    return config.thresholdValue - config.parameter1 *
                        (float)Math.Log(
                            1f + config.parameter0 * (config.thresholdValue - value));
                case CompressionKernel.LowerDirectSoftCap:
                    var lowerDelta = config.thresholdValue - value;
                    return config.thresholdValue -
                        config.parameter0 * lowerDelta / (lowerDelta + config.parameter0);
                default:
                    return value;
            }
        }

        private static float ApplyStaticLower(ref CompiledStatConfig config, float value)
        {
            var safeValue = value < 1e-10f ? 1e-10f : value;
            var excess = config.baseline / safeValue - config.thresholdFactor;
            float compressedExcess;
            switch (config.kernel)
            {
                case CompressionKernel.LowerLinear:
                    compressedExcess = excess * config.parameter0;
                    break;
                case CompressionKernel.LowerPower:
                    compressedExcess = (float)Math.Pow(excess + 1f, config.parameter1) - 1f;
                    break;
                case CompressionKernel.LowerLogarithmic:
                    compressedExcess = (float)Math.Log(1f + config.parameter0 * excess) * config.parameter1;
                    break;
                case CompressionKernel.LowerSoftCap:
                    compressedExcess = config.parameter0 * excess / (excess + config.parameter0);
                    break;
                default:
                    return value;
            }

            return config.baseline / (config.thresholdFactor + compressedExcess);
        }

    }
}

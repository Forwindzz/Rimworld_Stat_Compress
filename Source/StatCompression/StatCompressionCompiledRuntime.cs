using System;
using System.Reflection;
using System.Reflection.Emit;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal delegate float StatCompressor(float value);

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
        LowerSoftCap
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

    internal sealed class StatCompressionRuntimePlan
    {
        public readonly CompiledStatConfig[] configsByIndex;
        public readonly StatCompressor[] dynamicCompressorsByIndex;

        public StatCompressionRuntimePlan(
            CompiledStatConfig[] configsByIndex,
            StatCompressor[] dynamicCompressorsByIndex)
        {
            this.configsByIndex = configsByIndex;
            this.dynamicCompressorsByIndex = dynamicCompressorsByIndex;
        }
    }

    internal static class StatCompressionRuntimeCompiler
    {
        private static readonly MethodInfo MathLogMethod =
            typeof(Math).GetMethod(nameof(Math.Log), new[] { typeof(double) });

        private static readonly MethodInfo MathPowMethod =
            typeof(Math).GetMethod(nameof(Math.Pow), new[] { typeof(double), typeof(double) });

        public static StatCompressionRuntimePlan Compile(
            StatCompressionSettings settings,
            bool buildDynamicMethods)
        {
            var allStats = DefDatabase<StatDef>.AllDefsListForReading;
            var count = allStats.NullOrEmpty() ? 0 : allStats.Count;
            var compiled = new CompiledStatConfig[count];
            var dynamicCompressors = new StatCompressor[count];
            var generated = 0;
            var failed = 0;

            for (var i = 0; i < count; i++)
            {
                var stat = allStats[i];
                var config = settings.GetConfigFast(stat);
                compiled[stat.index] = CompileConfig(settings, config);

                if (!buildDynamicMethods || compiled[stat.index].kernel == CompressionKernel.Disabled)
                {
                    continue;
                }

                try
                {
                    dynamicCompressors[stat.index] = CreateDynamicCompressor(stat, ref compiled[stat.index]);
                    generated++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Warning($"[{StatCompressionConstants.DisplayName}] DynamicMethod generation failed for {stat.defName}; using compiled static fallback. {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (buildDynamicMethods)
            {
                Log.Message($"[{StatCompressionConstants.DisplayName}] Runtime plan compiled: stats={count}, dynamicMethods={generated}, dynamicFallbacks={failed}.");
            }

            return new StatCompressionRuntimePlan(compiled, dynamicCompressors);
        }

        private static CompiledStatConfig CompileConfig(
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
                settings.parameter,
                config.tScale);
            var higher = config.direction == StatCompressionDirection.HigherIsBetter;
            var result = new CompiledStatConfig
            {
                kernel = KernelFor(config.method, higher),
                thresholdValue = higher ? baseline * threshold : baseline / threshold,
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
                    result.parameter0 = higher ? result.invBaseline : actualParameter;
                    result.parameter1 = actualParameter;
                    break;
                case CompressionMethod.Logarithmic:
                    var logarithmicStrength = (float)Math.Log(actualParameter);
                    result.parameter0 = higher
                        ? logarithmicStrength * result.invBaseline
                        : logarithmicStrength;
                    result.parameter1 = higher
                        ? baseline / logarithmicStrength
                        : 1f / logarithmicStrength;
                    break;
                case CompressionMethod.SoftCap:
                    result.parameter0 = higher ? baseline * actualParameter : actualParameter;
                    break;
            }

            return result;
        }

        private static CompressionKernel KernelFor(CompressionMethod method, bool higher)
        {
            if (higher)
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
            else
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

            return CompressionKernel.Disabled;
        }

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

            if (float.IsNaN(value))
            {
                return true;
            }

            return value >= 0f && value < config.thresholdValue;
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
                default:
                    return value;
            }
        }

        private static float ApplyStaticLower(ref CompiledStatConfig config, float value)
        {
            var excess = config.baseline / value - config.thresholdFactor;
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

        private static StatCompressor CreateDynamicCompressor(StatDef stat, ref CompiledStatConfig config)
        {
            var method = new DynamicMethod(
                "StatCompression_" + stat.index + "_" + config.kernel,
                typeof(float),
                new[] { typeof(float) },
                typeof(StatCompressionRuntimeCompiler).Module,
                true);
            var il = method.GetILGenerator();

            if (config.kernel <= CompressionKernel.HigherSoftCap)
            {
                EmitHigherFormula(il, ref config);
            }
            else
            {
                EmitLowerFormula(il, ref config);
            }

            return (StatCompressor)method.CreateDelegate(typeof(StatCompressor));
        }

        private static void EmitHigherFormula(ILGenerator il, ref CompiledStatConfig config)
        {
            switch (config.kernel)
            {
                case CompressionKernel.HigherLinear:
                    EmitFloat(il, config.thresholdValue);
                    il.Emit(OpCodes.Ldarg_0);
                    EmitFloat(il, config.thresholdValue);
                    il.Emit(OpCodes.Sub);
                    EmitFloat(il, config.parameter0);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ret);
                    return;
                case CompressionKernel.HigherPower:
                    EmitFloat(il, config.thresholdValue);
                    EmitFloat(il, config.baseline);
                    il.Emit(OpCodes.Ldc_R8, 1d);
                    il.Emit(OpCodes.Ldarg_0);
                    EmitFloat(il, config.thresholdValue);
                    il.Emit(OpCodes.Sub);
                    EmitFloat(il, config.parameter0);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_R8, (double)config.parameter1);
                    il.Emit(OpCodes.Call, MathPowMethod);
                    il.Emit(OpCodes.Ldc_R8, 1d);
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Conv_R4);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ret);
                    return;
                case CompressionKernel.HigherLogarithmic:
                    EmitFloat(il, config.thresholdValue);
                    EmitFloat(il, config.parameter1);
                    il.Emit(OpCodes.Ldc_R8, 1d);
                    EmitFloat(il, config.parameter0);
                    il.Emit(OpCodes.Ldarg_0);
                    EmitFloat(il, config.thresholdValue);
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Call, MathLogMethod);
                    il.Emit(OpCodes.Conv_R4);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ret);
                    return;
                case CompressionKernel.HigherSoftCap:
                    EmitFloat(il, config.thresholdValue);
                    EmitFloat(il, config.parameter0);
                    il.Emit(OpCodes.Ldarg_0);
                    EmitFloat(il, config.thresholdValue);
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Ldarg_0);
                    EmitFloat(il, config.thresholdValue);
                    il.Emit(OpCodes.Sub);
                    EmitFloat(il, config.parameter0);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Div);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ret);
                    return;
                default:
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ret);
                    return;
            }
        }

        private static void EmitLowerFormula(ILGenerator il, ref CompiledStatConfig config)
        {
            var excess = il.DeclareLocal(typeof(float));
            EmitFloat(il, config.baseline);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Div);
            EmitFloat(il, config.thresholdFactor);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, excess);

            EmitFloat(il, config.baseline);
            EmitFloat(il, config.thresholdFactor);
            switch (config.kernel)
            {
                case CompressionKernel.LowerLinear:
                    il.Emit(OpCodes.Ldloc, excess);
                    EmitFloat(il, config.parameter0);
                    il.Emit(OpCodes.Mul);
                    break;
                case CompressionKernel.LowerPower:
                    il.Emit(OpCodes.Ldc_R8, 1d);
                    il.Emit(OpCodes.Ldloc, excess);
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Ldc_R8, (double)config.parameter1);
                    il.Emit(OpCodes.Call, MathPowMethod);
                    il.Emit(OpCodes.Ldc_R8, 1d);
                    il.Emit(OpCodes.Sub);
                    il.Emit(OpCodes.Conv_R4);
                    break;
                case CompressionKernel.LowerLogarithmic:
                    il.Emit(OpCodes.Ldc_R8, 1d);
                    EmitFloat(il, config.parameter0);
                    il.Emit(OpCodes.Ldloc, excess);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Call, MathLogMethod);
                    il.Emit(OpCodes.Conv_R4);
                    EmitFloat(il, config.parameter1);
                    il.Emit(OpCodes.Mul);
                    break;
                case CompressionKernel.LowerSoftCap:
                    EmitFloat(il, config.parameter0);
                    il.Emit(OpCodes.Ldloc, excess);
                    il.Emit(OpCodes.Mul);
                    il.Emit(OpCodes.Ldloc, excess);
                    EmitFloat(il, config.parameter0);
                    il.Emit(OpCodes.Add);
                    il.Emit(OpCodes.Div);
                    break;
                default:
                    il.Emit(OpCodes.Ldc_R4, 0f);
                    break;
            }

            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Div);
            il.Emit(OpCodes.Ret);
        }

        private static void EmitFloat(ILGenerator il, float value)
        {
            il.Emit(OpCodes.Ldc_R4, value);
        }
    }
}

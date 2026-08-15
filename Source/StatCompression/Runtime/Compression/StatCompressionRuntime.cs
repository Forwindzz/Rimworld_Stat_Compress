using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionRuntime
    {
        private static CompiledStatConfig[] activeConfigsByIndex = new CompiledStatConfig[0];

        public static int PlanVersion { get; private set; }

        public static void RebuildRuntimePlan(StatCompressionSettings settings)
        {
            activeConfigsByIndex = StatCompressionRuntimeCompiler.Compile(settings);
            PlanVersion++;
        }

        internal static ref CompiledStatConfig ConfigSlot(StatDef stat)
        {
            return ref activeConfigsByIndex[stat.index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Compress(
            StatDef stat,
            StatRequest request,
            ref float value)
        {
            var configs = activeConfigsByIndex;
            ref var config = ref configs[stat.index];
            if (!StatCompressionRuntimeCompiler.ShouldCompress(ref config, value))
            {
                return;
            }

            var targets = ObjectTargetFilterRuntime.Active;
            if (!targets.matchAll &&
                !ObjectTargetFilterRuntime.MatchesFiltered(targets, request))
            {
                return;
            }

            value = StatCompressionRuntimeCompiler.ApplyStaticUnchecked(ref config, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CompressBeforePostProcess(StatDef stat, StatRequest request, ref float value)
        {
            var configs = activeConfigsByIndex;
            ref var config = ref configs[stat.index];
            if (!StatCompressionRuntimeCompiler.ShouldCompress(ref config, value))
            {
                return;
            }

            var targets = ObjectTargetFilterRuntime.Active;
            if (!targets.matchAll &&
                !ObjectTargetFilterRuntime.MatchesFiltered(targets, request))
            {
                return;
            }

            if (StatCompressionExplanationRuntime.HasActiveContext)
            {
                StatCompressionExplanationRuntime.TryCaptureBeforePostProcess(stat, request, value);
            }
            value = StatCompressionRuntimeCompiler.ApplyStaticUnchecked(ref config, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CanRun(StatCompressionSettings settings, bool applyPostProcess)
        {
            return applyPostProcess && settings.enabled;
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
    }
}

using System;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionExplanationRuntime
    {
        private static ExplanationContext current;
        private static ExplanationValueCache valueCache;

        public static bool HasActiveContext
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => current != null;
        }

        internal sealed class ExplanationContext
        {
            public ExplanationContext parent;
            public StatCompressionSettings settings;
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

        public static ExplanationContext Begin(
            StatCompressionSettings settings,
            StatDef stat,
            StatRequest request)
        {
            if (!settings.enabled)
            {
                return null;
            }

            var config = settings.GetConfigFast(stat);
            if (!config.enabled)
            {
                return null;
            }

            var targets = ObjectTargetFilterRuntime.Active;
            if (!targets.matchAll &&
                !ObjectTargetFilterRuntime.MatchesFiltered(targets, request))
            {
                return null;
            }

            var context = new ExplanationContext
            {
                parent = current,
                settings = settings,
                stat = stat,
                request = request,
                config = config
            };
            current = context;
            return context;
        }

        public static void End(ExplanationContext context)
        {
            current = context.parent;
        }

        public static bool TryBuild(
            ExplanationContext context,
            float finalValue,
            out string explanation)
        {
            explanation = null;
            if (!TryGetUncompressedFinal(context, finalValue, out var original))
            {
                return false;
            }

            if (Math.Abs(original - finalValue) < 0.000001f)
            {
                return false;
            }

            explanation = StatCompressionExplanationFormatter.Build(
                context.settings,
                context.stat,
                context.config,
                original,
                finalValue,
                context.compressionInputCaptured,
                context.compressionInput,
                context.compressionOutput);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TryCaptureBeforePostProcess(
            StatDef stat,
            StatRequest request,
            float value)
        {
            var context = current;
            if (context == null ||
                !context.captureCompressionInput ||
                context.compressionInputCaptured ||
                context.stat != stat ||
                !context.request.Equals(request))
            {
                return;
            }

            context.compressionInput = value;
            var captureConfig = context.captureConfig;
            context.compressionOutput = StatCompressionRuntimeCompiler.ApplyStatic(
                ref captureConfig,
                value);
            context.compressionInputCaptured = true;
        }

        private static bool TryGetUncompressedFinal(
            ExplanationContext context,
            float finalValue,
            out float uncompressedValue)
        {
            var stat = context.stat;
            var request = context.request;
            var gameTick = Find.TickManager?.TicksGame ?? -1;
            var cache = valueCache;
            if (cache != null &&
                cache.statIndex == stat.index &&
                cache.request.Equals(request) &&
                cache.finalValue.Equals(finalValue) &&
                cache.gameTick == gameTick &&
                cache.planVersion == StatCompressionRuntime.PlanVersion)
            {
                uncompressedValue = cache.uncompressedValue;
                context.compressionInputCaptured = cache.compressionInputCaptured;
                context.compressionInput = cache.compressionInput;
                context.compressionOutput = cache.compressionOutput;
                return true;
            }

            ref var config = ref StatCompressionRuntime.ConfigSlot(stat);
            var previousConfig = config;
            try
            {
                context.captureConfig = previousConfig;
                context.captureCompressionInput =
                    StatCompressionBootstrap.ActiveStage == CompressionStage.BeforePostProcessCurve;
                context.compressionInputCaptured = false;
                config.kernel = CompressionKernel.Disabled;
                uncompressedValue = stat.Worker.GetValue(request, true);
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

            valueCache = new ExplanationValueCache
            {
                statIndex = stat.index,
                request = request,
                finalValue = finalValue,
                gameTick = gameTick,
                planVersion = StatCompressionRuntime.PlanVersion,
                uncompressedValue = uncompressedValue,
                compressionInputCaptured = context.compressionInputCaptured,
                compressionInput = context.compressionInput,
                compressionOutput = context.compressionOutput
            };
            return true;
        }
    }
}

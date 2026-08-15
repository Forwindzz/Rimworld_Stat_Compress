using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal static class BodyPartHealthCompressionRuntime
    {
        private static CompiledStatConfig activeConfig;
        private static bool active;
        private static CompressionMethod method;
        private static float actualParameter;
        private static readonly HashSet<string> RawReadWarningKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public static bool Active => active;

        public static void ResetRawReadWarnings()
        {
            RawReadWarningKeys.Clear();
        }

        public static void Rebuild(StatCompressionSettings settings)
        {
            var config = settings.BodyPartHealthConfig;
            active = settings.enabled && config.enabled;
            method = StatCompressionRuntime.ResolveMethod(config.method, settings.method);
            actualParameter = StatCompressionRuntime.GetActualParameter(
                config.method,
                settings.method,
                settings.parameter,
                config.tScale);
            if (!active)
            {
                activeConfig = new CompiledStatConfig { kernel = CompressionKernel.Disabled };
                return;
            }

            activeConfig = StatCompressionRuntimeCompiler.CompileConfig(settings, config);
        }

        public static void Disable()
        {
            active = false;
            activeConfig = new CompiledStatConfig { kernel = CompressionKernel.Disabled };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Compress(BodyPartDef partDef, Pawn pawn, float rawValue)
        {
            if (!active)
            {
                return rawValue;
            }

            var targets = ObjectTargetFilterRuntime.Active;
            if (!targets.matchAll &&
                !ObjectTargetFilterRuntime.MatchesPawnFiltered(targets, pawn))
            {
                return rawValue;
            }

            var baseline = NaturalBaseline(partDef, pawn);
            var normalized = rawValue / baseline;
            var compressed = StatCompressionRuntimeCompiler.ApplyStatic(ref activeConfig, normalized);
            if (compressed == normalized)
            {
                return rawValue;
            }

            return Mathf.Max(1f, Mathf.CeilToInt(baseline * compressed));
        }

        public static bool TryBuildExplanation(Pawn pawn, BodyPartRecord part, out string explanation)
        {
            explanation = null;
            if (!active || part == null || pawn == null)
            {
                return false;
            }

            var targets = ObjectTargetFilterRuntime.Active;
            if (!targets.matchAll &&
                !ObjectTargetFilterRuntime.MatchesPawnFiltered(targets, pawn))
            {
                return false;
            }

            if (
                !BodyPartHealthCompressionModule.TryGetRawMaxHealth(part, pawn, out var rawValue))
            {
                return false;
            }

            var finalValue = Compress(part.def, pawn, rawValue);
            if (finalValue == rawValue)
            {
                return false;
            }

            var baseline = NaturalBaseline(part.def, pawn) * activeConfig.baseline;
            var text =
                StatCompressionText.T("StatCompression_Explanation_Separator") + "\n" +
                StatCompressionText.T(
                    "StatCompression_BodyPartHealth_Explanation_ValueLine",
                    rawValue.ToString("0.###"),
                    finalValue.ToString("0.###")) + "\n" +
                StatCompressionText.T(
                    "StatCompression_BodyPartHealth_Explanation_MethodLine",
                    StatCompressionText.MethodLabel(method),
                    actualParameter.ToString("0.###"),
                    baseline.ToString("0.###"));

            var hint = MethodHint(method);
            if (!hint.NullOrEmpty())
            {
                text += "\n" + hint;
            }

            explanation = text.Colorize(ColoredText.SubtleGrayColor);
            return true;
        }

        public static bool TryReadRawVanilla(BodyPartRecord part, Pawn pawn, out float value)
        {
            var previousKernel = activeConfig.kernel;
            try
            {
                activeConfig.kernel = CompressionKernel.Disabled;
                value = part.def.GetMaxHealth(pawn);
                return true;
            }
            catch (Exception ex)
            {
                value = 0f;
                WarnRawReadFailure("Vanilla", ex);
                return false;
            }
            finally
            {
                activeConfig.kernel = previousKernel;
            }
        }

        public static bool TryReadRawEbf(BodyPartRecord part, Pawn pawn, out float value)
        {
            var previousKernel = activeConfig.kernel;
            try
            {
                activeConfig.kernel = CompressionKernel.Disabled;
                value = EbfBodyPartHealthAdapter.GetRawMaxHealth(part, pawn);
                return true;
            }
            catch (Exception ex)
            {
                value = 0f;
                WarnRawReadFailure("EBF", ex);
                return false;
            }
            finally
            {
                activeConfig.kernel = previousKernel;
            }
        }

        private static void WarnRawReadFailure(string backend, Exception exception)
        {
            var key = backend + "\n" + exception.GetType().FullName + "\n" + exception.Message;
            if (!RawReadWarningKeys.Add(key))
            {
                return;
            }

            Log.Warning(
                $"[{StatCompressionConstants.DisplayName}] Failed to read uncompressed " +
                $"body-part health through {backend}: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float NaturalBaseline(BodyPartDef partDef, Pawn pawn)
        {
            return Mathf.CeilToInt(
                partDef.hitPoints *
                pawn.ageTracker.CurLifeStage.healthScaleFactor *
                pawn.RaceProps.baseHealthScale);
        }

        private static string MethodHint(CompressionMethod compressionMethod)
        {
            switch (compressionMethod)
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

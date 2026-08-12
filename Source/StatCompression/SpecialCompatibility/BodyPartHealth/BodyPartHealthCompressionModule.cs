using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal enum BodyPartHealthBackend : byte
    {
        None,
        Vanilla,
        EliteBionicsFramework,
        Failed
    }

    internal static class BodyPartHealthCompressionModule
    {
        private const string HarmonyId = StatCompressionConstants.PackageId + ".bodyparthealth";

        private static bool initialized;
        private static BodyPartHealthBackend backend;

        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            BodyPartHealthCompressionRuntime.Rebuild(StatCompressionMod.Settings);
            var harmony = new Harmony(HarmonyId);

            try
            {
                if (AccessTools.TypeByName("EBF.EBFEndpoints") != null)
                {
                    if (!EbfBodyPartHealthAdapter.TryInstall(harmony, out var error))
                    {
                        backend = BodyPartHealthBackend.Failed;
                        BodyPartHealthCompressionRuntime.Disable();
                        Log.Error(
                            $"[{StatCompressionConstants.DisplayName}] EBF was detected, but body-part health " +
                            $"compression Postfix could not be installed: {error}. " +
                            $"The module is disabled; vanilla fallback was not installed.");
                        return;
                    }

                    backend = BodyPartHealthBackend.EliteBionicsFramework;
                }
                else
                {
                    VanillaBodyPartHealthPatch.Install(harmony);
                    backend = BodyPartHealthBackend.Vanilla;
                }

                BodyPartHealthTooltipPatch.Install(harmony);
                Log.Message(
                    $"[{StatCompressionConstants.DisplayName}] Body-part health compatibility backend: {backend}; " +
                    $"enabled={BodyPartHealthCompressionRuntime.Active}.");
            }
            catch (Exception ex)
            {
                backend = BodyPartHealthBackend.Failed;
                BodyPartHealthCompressionRuntime.Disable();
                Log.Error(
                    $"[{StatCompressionConstants.DisplayName}] Failed to install body-part health compatibility module. " +
                    $"The module is disabled.\n{ex}");
            }
        }

        public static void NotifySettingsChanged(StatCompressionSettings settings)
        {
            if (initialized && backend == BodyPartHealthBackend.Failed)
            {
                BodyPartHealthCompressionRuntime.Disable();
                return;
            }

            BodyPartHealthCompressionRuntime.Rebuild(settings);
        }

        public static bool TryGetRawMaxHealth(BodyPartRecord part, Pawn pawn, out float value)
        {
            switch (backend)
            {
                case BodyPartHealthBackend.Vanilla:
                    return BodyPartHealthCompressionRuntime.TryReadRawVanilla(part, pawn, out value);
                case BodyPartHealthBackend.EliteBionicsFramework:
                    return BodyPartHealthCompressionRuntime.TryReadRawEbf(part, pawn, out value);
                default:
                    value = 0f;
                    return false;
            }
        }
    }
}

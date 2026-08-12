using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StatCompression
{
    public static class StatCompressionBootstrap
    {
        private static bool patched;

        public static void PatchAll()
        {
            if (patched)
            {
                return;
            }

            patched = true;
            var harmony = new Harmony(StatCompressionConstants.PackageId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            ReportFallbackIfNeeded();

            Log.Message($"[{StatCompressionConstants.DisplayName}] {StatCompressionConstants.Version} loaded.");
        }

        private static void ReportFallbackIfNeeded()
        {
            var settings = StatCompressionMod.Settings;
            if (settings == null ||
                settings.stage != CompressionStage.BeforePostProcessCurve ||
                StatWorker_FinalizeValue_Patch.BeforePostProcessPatchApplied)
            {
                return;
            }

            var target = AccessTools.Method(typeof(StatWorker), nameof(StatWorker.FinalizeValue));
            var patchInfo = Harmony.GetPatchInfo(target);
            var owners = patchInfo == null
                ? "none"
                : string.Join(", ", patchInfo.Owners.OrderBy(owner => owner));
            var fallback = settings.autoFallbackToGlobalPostfix
                ? "GlobalPostfix fallback is active."
                : "GlobalPostfix fallback is disabled; compression will not run.";
            Log.Error(
                $"[{StatCompressionConstants.DisplayName}] Could not inject before StatWorker.FinalizeValue postProcessCurve. " +
                $"Matched curve IL blocks: {StatWorker_FinalizeValue_Patch.CurveBlockMatchCount}. " +
                $"{fallback} This fallback uses the final-value domain, so stats with postProcessCurve may require different baselines. " +
                $"FinalizeValue patch owners: {owners}");
        }
    }
}

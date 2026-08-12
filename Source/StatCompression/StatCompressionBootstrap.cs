using System;
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

        public static CompressionStage ActiveStage { get; private set; } = CompressionStage.GlobalPostfix;

        public static void PatchAll()
        {
            if (patched)
            {
                return;
            }

            patched = true;
            var harmony = new Harmony(StatCompressionConstants.PackageId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            PatchCompressionEntry(harmony);
            BodyPartHealthCompressionModule.Initialize();
            if (StaticConstructorOnStartupUtility.coreStaticAssetsLoaded)
            {
                BaseDamageCompressionModule.Initialize();
                HediffStageCompressionModule.Initialize();
            }

            Log.Message($"[{StatCompressionConstants.DisplayName}] {StatCompressionConstants.Version} loaded.");
        }

        private static void PatchCompressionEntry(Harmony harmony)
        {
            var settings = StatCompressionMod.Settings;
            if (settings.stage == CompressionStage.GlobalPostfix)
            {
                PatchGlobalPostfix(harmony);
                return;
            }

            var finalizeValue = AccessTools.Method(typeof(StatWorker), nameof(StatWorker.FinalizeValue));
            var transpiler = new HarmonyMethod(
                AccessTools.Method(
                    typeof(StatWorker_FinalizeValue_Patch),
                    nameof(StatWorker_FinalizeValue_Patch.Transpiler)))
            {
                priority = Priority.Last
            };
            Exception patchException = null;
            try
            {
                harmony.Patch(finalizeValue, transpiler: transpiler);
            }
            catch (Exception ex)
            {
                patchException = ex;
            }

            if (patchException == null && StatWorker_FinalizeValue_Patch.BeforePostProcessPatchApplied)
            {
                ActiveStage = CompressionStage.BeforePostProcessCurve;
                return;
            }

            harmony.Unpatch(
                finalizeValue,
                AccessTools.Method(
                    typeof(StatWorker_FinalizeValue_Patch),
                    nameof(StatWorker_FinalizeValue_Patch.Transpiler)));

            var patchInfo = Harmony.GetPatchInfo(finalizeValue);
            var owners = patchInfo == null
                ? "none"
                : string.Join(", ", patchInfo.Owners.OrderBy(owner => owner));
            if (settings.autoFallbackToGlobalPostfix)
            {
                PatchGlobalPostfix(harmony);
            }

            var fallback = settings.autoFallbackToGlobalPostfix
                ? "The failed transpiler was removed and only the GlobalPostfix fallback was installed."
                : "The failed transpiler was removed and no compression entry was installed.";
            var exceptionDetails = patchException == null
                ? string.Empty
                : $" Patch exception: {patchException.GetType().Name}: {patchException.Message}.";
            Log.Error(
                $"[{StatCompressionConstants.DisplayName}] Could not inject before StatWorker.FinalizeValue postProcessCurve. " +
                $"Matched curve IL blocks: {StatWorker_FinalizeValue_Patch.CurveBlockMatchCount}. " +
                $"{fallback} This fallback uses the final-value domain, so stats with postProcessCurve may require different baselines. " +
                $"FinalizeValue patch owners: {owners}.{exceptionDetails}");
        }

        private static void PatchGlobalPostfix(Harmony harmony)
        {
            var getValue = AccessTools.Method(
                typeof(StatWorker),
                nameof(StatWorker.GetValue),
                new[] { typeof(StatRequest), typeof(bool) });
            var postfix = new HarmonyMethod(
                AccessTools.Method(
                    typeof(StatWorker_GetValue_Patch),
                    nameof(StatWorker_GetValue_Patch.Postfix)))
            {
                priority = Priority.Last
            };
            harmony.Patch(getValue, postfix: postfix);
            ActiveStage = CompressionStage.GlobalPostfix;
        }
    }
}

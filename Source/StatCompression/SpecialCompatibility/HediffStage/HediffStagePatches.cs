using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StatCompression
{
    [HarmonyPatch(
        typeof(HediffStatsUtility),
        nameof(HediffStatsUtility.SpecialDisplayStats),
        new[] { typeof(HediffStage), typeof(Hediff) })]
    internal static class HediffStageSpecialDisplayStatsPatch
    {
        private static void Postfix(
            HediffStage stage,
            Hediff instance,
            ref IEnumerable<StatDrawEntry> __result)
        {
            __result = HediffStageCompressionModule.AppendInfoEntries(__result, stage, instance);
        }
    }

    [HarmonyPatch(typeof(Hediff), nameof(Hediff.TipStringExtra), MethodType.Getter)]
    internal static class HediffStageTooltipPatch
    {
        private static void Postfix(Hediff __instance, ref string __result)
        {
            __result = HediffStageCompressionModule.AppendTooltipDetails(
                __result,
                __instance.CurStage);
        }
    }
}

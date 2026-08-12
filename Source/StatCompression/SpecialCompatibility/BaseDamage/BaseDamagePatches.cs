using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StatCompression
{
    [HarmonyPatch(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll))]
    internal static class BaseDamageInitializationPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            // Def post-processors registered by static constructors can replace verb/projectile objects.
            // Defer twice so direct and nested ExecuteWhenFinished callbacks complete before capture.
            LongEventHandler.ExecuteWhenFinished(() =>
                LongEventHandler.ExecuteWhenFinished(BaseDamageCompressionModule.Initialize));
        }
    }

    [HarmonyPatch(typeof(Def), nameof(Def.SpecialDisplayStats))]
    internal static class BaseDamageDefInfoPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Def __instance, ref IEnumerable<StatDrawEntry> __result)
        {
            __result = BaseDamageCompressionModule.AppendInfoEntries(__result, __instance);
        }
    }

    [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.SpecialDisplayStats))]
    internal static class BaseDamageThingDefReportPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ThingDef __instance,
            ref IEnumerable<StatDrawEntry> __result)
        {
            __result = BaseDamageCompressionModule.AppendThingDefDamageReports(__result, __instance);
        }
    }

    [HarmonyPatch(typeof(AbilityDef), nameof(AbilityDef.SpecialDisplayStats))]
    internal static class BaseDamageAbilityDefInfoPatch
    {
        [HarmonyPostfix]
        private static void Postfix(AbilityDef __instance, ref IEnumerable<StatDrawEntry> __result)
        {
            __result = BaseDamageCompressionModule.AppendInfoEntries(__result, __instance);
        }
    }

    [HarmonyPatch(typeof(Hediff), nameof(Hediff.SpecialDisplayStats))]
    internal static class BaseDamageHediffInfoPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Hediff __instance, ref IEnumerable<StatDrawEntry> __result)
        {
            __result = BaseDamageCompressionModule.AppendInfoEntries(__result, __instance.def);
        }
    }
}

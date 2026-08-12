using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class VanillaBodyPartHealthPatch
    {
        private static readonly MethodInfo Target =
            AccessTools.Method(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth), new[] { typeof(Pawn) });

        private static readonly MethodInfo PostfixMethod =
            AccessTools.Method(typeof(VanillaBodyPartHealthPatch), nameof(Postfix));

        public static void Install(Harmony harmony)
        {
            harmony.Patch(
                Target,
                postfix: new HarmonyMethod(PostfixMethod) { priority = Priority.Last });
        }

        public static void Postfix(BodyPartDef __instance, Pawn pawn, ref float __result)
        {
            __result = BodyPartHealthCompressionRuntime.Compress(__instance, pawn, __result);
        }
    }

    internal static class BodyPartHealthTooltipPatch
    {
        private static readonly MethodInfo Target =
            AccessTools.Method(
                typeof(HealthCardUtility),
                "GetTooltip",
                new[] { typeof(Pawn), typeof(BodyPartRecord) });

        private static readonly MethodInfo PostfixMethod =
            AccessTools.Method(typeof(BodyPartHealthTooltipPatch), nameof(Postfix));

        public static void Install(Harmony harmony)
        {
            harmony.Patch(
                Target,
                postfix: new HarmonyMethod(PostfixMethod) { priority = Priority.Last });
        }

        public static void Postfix(Pawn pawn, BodyPartRecord part, ref string __result)
        {
            if (BodyPartHealthCompressionRuntime.TryBuildExplanation(pawn, part, out var explanation))
            {
                __result = __result.TrimEnd() + "\n" + explanation;
            }
        }
    }
}

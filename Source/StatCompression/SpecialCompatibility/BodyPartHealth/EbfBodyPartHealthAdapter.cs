using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace StatCompression
{
    internal static class EbfBodyPartHealthAdapter
    {
        private delegate float GetMaxHealthWithEbfDelegate(BodyPartRecord part, Pawn pawn, bool useCache);

        private static readonly MethodInfo PostfixMethod =
            AccessTools.Method(typeof(EbfBodyPartHealthAdapter), nameof(Postfix));

        private static MethodInfo endpointMethod;
        private static GetMaxHealthWithEbfDelegate getMaxHealth;

        public static bool TryInstall(Harmony harmony, out string error)
        {
            error = null;
            try
            {
                var endpointType = AccessTools.TypeByName("EBF.EBFEndpoints");
                endpointMethod = AccessTools.Method(
                    endpointType,
                    "GetMaxHealthWithEBF",
                    new[] { typeof(BodyPartRecord), typeof(Pawn), typeof(bool) });
                if (endpointMethod == null)
                {
                    error = "required EBF GetMaxHealthWithEBF endpoint was not found";
                    return false;
                }

                getMaxHealth = (GetMaxHealthWithEbfDelegate)Delegate.CreateDelegate(
                    typeof(GetMaxHealthWithEbfDelegate),
                    endpointMethod);
                harmony.Patch(
                    endpointMethod,
                    postfix: new HarmonyMethod(PostfixMethod) { priority = Priority.Last });
                return true;
            }
            catch (Exception ex)
            {
                if (endpointMethod != null)
                {
                    harmony.Unpatch(endpointMethod, PostfixMethod);
                }

                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public static float GetRawMaxHealth(BodyPartRecord part, Pawn pawn)
        {
            return getMaxHealth(part, pawn, false);
        }

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(BodyPartRecord __0, Pawn __1, ref float __result)
        {
            __result = BodyPartHealthCompressionRuntime.Compress(__0.def, __1, __result);
        }
    }
}

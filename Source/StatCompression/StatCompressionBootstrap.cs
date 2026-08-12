using System.Reflection;
using HarmonyLib;
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

            Log.Message($"[{StatCompressionConstants.DisplayName}] {StatCompressionConstants.Version} loaded.");
        }
    }
}

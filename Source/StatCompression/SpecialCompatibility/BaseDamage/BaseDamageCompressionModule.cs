using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class BaseDamageCompressionModule
    {
        private static bool initialized;

        public static void Initialize(StatCompressionSettings settings)
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            try
            {
                BaseDamageDefStore.Scan();
                BaseDamageDefStore.Apply(settings);
                BaseDamageReportCache.Rebuild(settings);
                Log.Message(
                    $"[{StatCompressionConstants.DisplayName}] Base-damage Def module initialized: " +
                    $"fields={BaseDamageDefStore.RecordCount}, " +
                    $"owners={BaseDamageDefStore.OwnerCount}, " +
                    $"changed={BaseDamageDefStore.ChangedCount}.");
            }
            catch (Exception ex)
            {
                BaseDamageDefStore.Restore();
                BaseDamageDefStore.RefreshDerivedStats();
                BaseDamageReportCache.Clear();
                Log.Error(
                    $"[{StatCompressionConstants.DisplayName}] Failed to initialize base-damage Def compression. " +
                    $"All captured fields were restored.\n{ex}");
            }
        }

        public static void NotifySettingsChanged(StatCompressionSettings settings)
        {
            if (!initialized)
            {
                return;
            }

            BaseDamageDefStore.Restore();
            BaseDamageDefStore.Scan();
            BaseDamageDefStore.Apply(settings);
            BaseDamageReportCache.Rebuild(settings);
        }

        public static IEnumerable<StatDrawEntry> AppendInfoEntries(
            IEnumerable<StatDrawEntry> original,
            Def owner)
        {
            return BaseDamageReportCache.AppendInfoEntries(original, owner);
        }

        public static IEnumerable<StatDrawEntry> AppendThingDefDamageReports(
            IEnumerable<StatDrawEntry> original,
            ThingDef owner)
        {
            return BaseDamageReportCache.AppendThingDefDamageReports(original, owner);
        }
    }
}

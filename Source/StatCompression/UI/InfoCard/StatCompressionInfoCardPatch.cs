using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    [HarmonyPatch(typeof(Dialog_InfoCard), nameof(Dialog_InfoCard.DoWindowContents))]
    internal static class StatCompressionInfoCardPatch
    {
        private const float ButtonWidth = 190f;
        private const float ButtonHeight = 35f;
        private const float BottomMargin = 2f;

        private static readonly AccessTools.FieldRef<Dialog_InfoCard, Dialog_InfoCard.InfoCardTab>
            TabField;
        private static readonly AccessTools.FieldRef<StatDrawEntry> VanillaSelectedEntryField;
        private static readonly bool TabFieldAvailable;
        private static readonly bool VanillaSelectedEntryAvailable;

        static StatCompressionInfoCardPatch()
        {
            var tabInfo = AccessTools.Field(typeof(Dialog_InfoCard), "tab");
            if (tabInfo != null)
            {
                TabField = AccessTools.FieldRefAccess<Dialog_InfoCard, Dialog_InfoCard.InfoCardTab>(tabInfo);
                TabFieldAvailable = true;
            }
            else
            {
                Log.Warning(
                    $"[{StatCompressionConstants.DisplayName}] Dialog_InfoCard.tab was not found. " +
                    "The InfoCard stat-settings shortcut is disabled.");
            }

            var selectedInfo = AccessTools.Field(typeof(StatsReportUtility), "selectedEntry");
            if (selectedInfo != null)
            {
                VanillaSelectedEntryField =
                    AccessTools.StaticFieldRefAccess<StatDrawEntry>(selectedInfo);
                VanillaSelectedEntryAvailable = true;
            }
            else
            {
                Log.Warning(
                    $"[{StatCompressionConstants.DisplayName}] StatsReportUtility.selectedEntry was not found. " +
                    "The vanilla InfoCard stat-settings shortcut is disabled.");
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Dialog_InfoCard __instance, Rect inRect)
        {
            if (!StatCompressionMod.Settings.showInfoCardSettingsButton)
            {
                return;
            }

            if (!TabFieldAvailable || TabField(__instance) != Dialog_InfoCard.InfoCardTab.Stats)
            {
                return;
            }

            var selectedEntry = BetterInfoCardSelection.IsAvailable
                ? BetterInfoCardSelection.Get(__instance)
                : VanillaSelectedEntryAvailable
                    ? VanillaSelectedEntryField()
                    : null;
            var stat = selectedEntry?.stat;
            if (stat == null ||
                !StatCompressionMod.Settings.TryGetConfigFast(stat, out _))
            {
                return;
            }

            var buttonRect = new Rect(
                inRect.xMax - ButtonWidth,
                inRect.yMax - ButtonHeight - BottomMargin,
                ButtonWidth,
                ButtonHeight);
            if (Widgets.ButtonText(buttonRect, StatCompressionText.T("StatCompression_OpenStatSettings")))
            {
                Find.WindowStack.Add(
                    new StatCompressionAdvancedSettingsWindow(StatCompressionMod.Settings, stat.defName));
            }
        }

        private static class BetterInfoCardSelection
        {
            private static readonly FieldInfo InstancesField;
            private static readonly FieldInfo SelectedEntryField;

            public static bool IsAvailable => InstancesField != null && SelectedEntryField != null;

            static BetterInfoCardSelection()
            {
                var patchType = AccessTools.TypeByName("BetterInfoCard.Dialog_InfoCard_Patch");
                InstancesField = patchType == null ? null : AccessTools.Field(patchType, "infoCardStatsDic");

                var utilityType = AccessTools.TypeByName("BetterInfoCard.StatsReportUtility_Instanced");
                SelectedEntryField = utilityType == null ? null : AccessTools.Field(utilityType, "selectedEntry");
            }

            public static StatDrawEntry Get(Dialog_InfoCard dialog)
            {
                if (InstancesField?.GetValue(null) is not IDictionary instances ||
                    !instances.Contains(dialog))
                {
                    return null;
                }

                var utility = instances[dialog];
                return utility == null ? null : SelectedEntryField?.GetValue(utility) as StatDrawEntry;
            }
        }
    }
}

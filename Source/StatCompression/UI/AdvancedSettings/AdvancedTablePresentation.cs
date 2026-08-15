using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed partial class AdvancedTableComponent
    {
        private static void DrawSingleLineText(Rect rect, string value)
        {
            var oldWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.LabelFit(rect, value ?? string.Empty);
            Text.WordWrap = oldWrap;
        }

        private string BuildTooltip(AdvancedRowState row)
        {
            var config = row.Config;
            return StatCompressionText.T("StatCompression_Tooltip_Baseline", config.baseline) +
                   "\n" + StatCompressionText.T(
                       "StatCompression_Tooltip_Threshold",
                       (config.thresholdFactor * 100f).ToString("0.###")) +
                   "\n" + StatCompressionText.T(
                       "StatCompression_Tooltip_Method",
                       StatCompressionText.MethodLabel(config.method)) +
                   "\n" + StatCompressionText.T(
                       "StatCompression_Tooltip_TScale",
                       config.tScale) +
                   "\n" + StatCompressionText.T(
                       "StatCompression_Tooltip_Direction",
                       StatCompressionText.DirectionShortLabel(config.direction)) +
                   (row.MissingStat
                       ? "\n" + StatCompressionText.T("StatCompression_MissingStat_Tooltip")
                       : SpecialCompressionConfigs.IsSpecial(config.defName)
                       ? "\n" + StatCompressionText.T("StatCompression_Tooltip_SpecialModule") +
                         SpecialTooltip(config.defName)
                       : row.Stat == null
                           ? string.Empty
                           : "\n" + StatCompressionText.T(
                               "StatCompression_Tooltip_Category",
                               row.TypeLabel));
        }

        private static string SpecialTooltip(string defName)
        {
            if (defName == SpecialCompressionConfigs.BodyPartHealthDefName)
            {
                return "\n" + StatCompressionText.T(
                    "StatCompression_SP_BodyPartHealth_BaselineTooltip");
            }
            if (SpecialCompressionConfigs.IsDamage(defName))
            {
                return "\n" + StatCompressionText.T(
                    "StatCompression_SP_Damage_BaselineTooltip");
            }
            if (!SpecialCompressionConfigs.IsHediffStage(defName))
            {
                return string.Empty;
            }

            return "\n" + StatCompressionText.T(
                defName == SpecialCompressionConfigs.RegenerationRateDefName
                    ? "StatCompression_SP_RegenerationRate_BaselineTooltip"
                    : "StatCompression_SP_HediffStageFactor_BaselineTooltip");
        }

        private static string DirectionTooltip(AdvancedRowState row)
        {
            if (row.IsDamage)
            {
                return StatCompressionText.T("StatCompression_SP_Damage_DirectionTooltip");
            }
            if (row.IsHediffStage)
            {
                return StatCompressionText.T("StatCompression_SP_HediffStage_DirectionTooltip");
            }

            return StatCompressionText.T("StatCompression_DirectionTooltip");
        }

        private static string LabelFor(StatCompressionStatConfig config, StatDef stat)
        {
            return SpecialCompressionConfigs.IsSpecial(config.defName)
                ? SpecialCompressionConfigs.LabelFor(config.defName)
                : stat?.LabelCap.ToString() ??
                  StatCompressionText.T("StatCompression_MissingStat_Label");
        }

        private static string TypeLabelFor(StatCompressionStatConfig config, StatDef stat)
        {
            if (SpecialCompressionConfigs.IsSpecial(config.defName))
            {
                return StatCompressionText.T("StatCompression_Type_SpecialModule");
            }

            if (stat == null)
            {
                return StatCompressionText.T("StatCompression_Type_MissingStat");
            }

            var category = stat.category;
            if (category == null)
            {
                return StatCompressionText.T("StatCompression_Type_Uncategorized");
            }

            var label = category.LabelCap.ToString();
            return label.NullOrEmpty() ? category.defName : label;
        }

        private static bool GlobalInputEquals(
            GlobalCompressionInput left,
            GlobalCompressionInput right)
        {
            return left.Method == right.Method &&
                   left.Parameter.Equals(right.Parameter) &&
                   left.ThresholdFactor.Equals(right.ThresholdFactor);
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= 0.000001f;
        }
    }
}

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
        private AdvancedTableInteraction DrawVisibleRows(Rect outRect, Rect viewRect)
        {
            if (filteredRows.Count == 0)
            {
                return default(AdvancedTableInteraction);
            }

            var first = Math.Max(
                0,
                Mathf.FloorToInt(scrollPosition.y / RowHeight) - 1);
            var last = Math.Min(
                filteredRows.Count - 1,
                Mathf.CeilToInt((scrollPosition.y + outRect.height) / RowHeight) + 1);
            var selectionChanged = false;
            var configChanged = false;
            StatCompressionStatConfig changedConfig = null;
            for (var i = first; i <= last; i++)
            {
                var result = DrawRow(
                    new Rect(0f, i * RowHeight, viewRect.width, RowHeight),
                    filteredRows[i],
                    i);
                selectionChanged |= result.SelectionChanged;
                configChanged |= result.ConfigChanged;
                if (result.ChangedConfig != null)
                {
                    changedConfig = result.ChangedConfig;
                }
            }

            return new AdvancedTableInteraction(selectionChanged, configChanged, changedConfig);
        }

        private AdvancedTableInteraction DrawRow(
            Rect rect,
            AdvancedRowState row,
            int rowIndex)
        {
            var config = row.Config;
            if ((rowIndex & 1) != 0)
            {
                Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            }

            if (selectedDefName == config.defName)
            {
                Widgets.DrawBoxSolid(rect, new Color(0.24f, 0.42f, 0.52f, 0.28f));
            }
            else if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            var selectionChanged = false;
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                rect.Contains(Event.current.mousePosition) &&
                selectedDefName != config.defName)
            {
                selectedDefName = config.defName;
                selectionChanged = true;
            }

            var selectedForPreset = presetSelection.Contains(config.defName);
            Widgets.Checkbox(
                Col(rect, 0).position + new Vector2(2f, 3f),
                ref selectedForPreset);
            if (selectedForPreset)
            {
                presetSelection.Add(config.defName);
            }
            else
            {
                presetSelection.Remove(config.defName);
            }

            var typeRect = Col(rect, 1);
            var defRect = Col(rect, 2);
            var labelRect = Col(rect, 3);
            var previousColor = GUI.color;
            if (row.MissingStat)
            {
                GUI.color = MissingStatColor;
            }
            Text.Font = GameFont.Tiny;
            DrawSingleLineText(typeRect, row.TypeLabel);
            DrawSingleLineText(defRect, config.defName);
            DrawSingleLineText(labelRect, row.Label);
            Text.Font = GameFont.Small;
            GUI.color = previousColor;

            var changedFields = AdvancedConfigField.None;
            var enabled = config.enabled;
            Widgets.Checkbox(
                Col(rect, 4).position + new Vector2(2f, 3f),
                ref enabled);
            if (enabled != config.enabled)
            {
                config.enabled = enabled;
                changedFields |= AdvancedConfigField.Enabled;
            }

            if (Widgets.ButtonText(Col(rect, 5), row.MethodLabel))
            {
                OpenMethodMenu(row);
            }

            var parameterRect = Col(rect, 6);
            if (showActualParameter)
            {
                var oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(parameterRect, row.ActualParameterText);
                Text.Anchor = oldAnchor;
                TooltipHandler.TipRegion(parameterRect, row.ActualParameterTooltip);
            }
            else
            {
                var oldTScale = config.tScale;
                Widgets.TextFieldNumeric(
                    parameterRect,
                    ref config.tScale,
                    ref row.TScaleBuffer,
                    0.0001f,
                    float.MaxValue);
                if (!NearlyEqual(oldTScale, config.tScale))
                {
                    changedFields |= AdvancedConfigField.TScale;
                }
                TooltipHandler.TipRegion(
                    parameterRect,
                    StatCompressionText.T("StatCompression_TScaleTooltip"));
            }

            var oldBaseline = config.baseline;
            Widgets.TextFieldNumeric(
                Col(rect, 7),
                ref config.baseline,
                ref row.BaselineBuffer,
                1e-10f,
                float.MaxValue);
            if (!NearlyEqual(oldBaseline, config.baseline))
            {
                changedFields |= AdvancedConfigField.Baseline;
            }

            var thresholdPercent = config.thresholdFactor * 100f;
            var oldThreshold = thresholdPercent;
            var thresholdMinimum = config.direction == StatCompressionDirection.LowerIsBetter
                ? 0.0001f
                : float.MinValue;
            Widgets.TextFieldNumeric(
                Col(rect, 8),
                ref thresholdPercent,
                ref row.ThresholdBuffer,
                thresholdMinimum,
                float.MaxValue);
            if (!NearlyEqual(oldThreshold, thresholdPercent))
            {
                config.thresholdFactor = thresholdPercent / 100f;
                changedFields |= AdvancedConfigField.Threshold;
            }

            var directionRect = Col(rect, 9);
            if (row.FixedDirection)
            {
                var oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(directionRect, row.DirectionLabel);
                Text.Anchor = oldAnchor;
            }
            else if (Widgets.ButtonText(directionRect, row.DirectionLabel))
            {
                OpenDirectionMenu(row);
            }

            if (Mouse.IsOver(directionRect))
            {
                TooltipHandler.TipRegion(directionRect, DirectionTooltip(row));
            }
            if (Mouse.IsOver(typeRect))
            {
                TooltipHandler.TipRegion(typeRect, row.TypeLabel);
            }
            if (Mouse.IsOver(defRect))
            {
                TooltipHandler.TipRegion(defRect, BuildTooltip(row));
            }
            if (Mouse.IsOver(labelRect))
            {
                TooltipHandler.TipRegion(labelRect, BuildTooltip(row));
            }

            if (changedFields != AdvancedConfigField.None)
            {
                ApplyRowEdit(row, changedFields);
                return new AdvancedTableInteraction(selectionChanged, true, config);
            }

            return new AdvancedTableInteraction(selectionChanged, false, null);
        }

        private void ApplyRowEdit(
            AdvancedRowState row,
            AdvancedConfigField fields,
            bool forceBufferSync = false)
        {
            if (row.FixedDirection)
            {
                row.Config.direction = row.IsDamage
                    ? StatCompressionDirection.HigherIsBetter
                    : SpecialCompressionConfigs.DirectionForHediffStage(row.Config.defName);
            }

            var oldTScale = row.Config.tScale;
            var oldBaseline = row.Config.baseline;
            var oldThreshold = row.Config.thresholdFactor;
            StatCompressionSettings.NormalizeConfig(row.Config);
            if ((fields & AdvancedConfigField.Method) != 0)
            {
                row.MethodLabel = StatCompressionText.MethodShortLabel(row.Config.method);
            }
            if ((fields & AdvancedConfigField.Direction) != 0)
            {
                row.DirectionLabel = StatCompressionText.DirectionShortLabel(row.Config.direction);
            }
            if ((fields & AdvancedConfigField.TScale) != 0 &&
                (forceBufferSync || !NearlyEqual(oldTScale, row.Config.tScale)))
            {
                row.TScaleBuffer = row.Config.tScale.ToString();
            }
            if ((fields & AdvancedConfigField.Baseline) != 0 &&
                (forceBufferSync || !NearlyEqual(oldBaseline, row.Config.baseline)))
            {
                row.BaselineBuffer = row.Config.baseline.ToString();
            }
            if ((fields & (AdvancedConfigField.Threshold | AdvancedConfigField.Direction)) != 0 &&
                (forceBufferSync || !NearlyEqual(oldThreshold, row.Config.thresholdFactor)))
            {
                row.ThresholdBuffer = (row.Config.thresholdFactor * 100f).ToString();
            }
            if ((fields & AdvancedConfigField.Metadata) != 0)
            {
                RefreshMetadata(row);
                filterDirty = true;
            }

            if ((fields & (AdvancedConfigField.Method | AdvancedConfigField.TScale)) != 0 ||
                forceBufferSync)
            {
                RefreshActualParameter(row);
            }
        }

    }
}

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
        private void DrawHeader(Rect rect)
        {
            var groupRect = new Rect(rect.x, rect.y, rect.width, GroupHeaderHeight);
            var columnRect = new Rect(
                rect.x,
                rect.y + GroupHeaderHeight,
                rect.width,
                ColumnHeaderHeight);
            Widgets.DrawBoxSolid(groupRect, new Color(0.15f, 0.15f, 0.15f, 1f));
            Widgets.DrawBoxSolid(columnRect, new Color(0.18f, 0.18f, 0.18f, 1f));

            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.LabelFit(
                GroupRect(groupRect, 0, 4),
                StatCompressionText.T("StatCompression_Group_StatInfo"));
            Widgets.LabelFit(
                GroupRect(groupRect, 4, 7),
                StatCompressionText.T("StatCompression_Group_Compression"));
            Widgets.LabelFit(
                GroupRect(groupRect, 7, 10),
                StatCompressionText.T("StatCompression_Group_Trigger"));
            Text.Anchor = oldAnchor;

            Widgets.DrawLineHorizontal(
                groupRect.x,
                groupRect.yMax - 1f,
                groupRect.width,
                GroupLineColor);

            for (var i = 0; i < ColumnCount; i++)
            {
                var cell = Col(columnRect, i);
                if (Mouse.IsOver(cell))
                {
                    Widgets.DrawHighlight(cell);
                }

                var column = (SortColumn)i;
                var label = i == 6 && showActualParameter
                    ? StatCompressionText.T("StatCompression_Column_ActualT")
                    : columnLabels[i];
                if (i > 0 && sortColumn == column)
                {
                    label += sortAscending ? " ^" : " v";
                }

                var labelRect = cell.ContractedBy(2f);
                var oldWrap = Text.WordWrap;
                Text.WordWrap = false;
                Widgets.LabelFit(labelRect, label);
                Text.WordWrap = oldWrap;
                TooltipHandler.TipRegion(labelRect, label);
                if (Widgets.ButtonInvisible(cell, false))
                {
                    if (i == 0)
                    {
                        ToggleVisibleSelection();
                    }
                    else
                    {
                        SetSort(column);
                    }
                }
            }

            for (var i = 1; i < ColumnCount; i++)
            {
                var color = i == 4 || i == 7 ? GroupLineColor : ColumnLineColor;
                Widgets.DrawLine(
                    new Vector2(rect.x + columnOffsets[i], rect.y),
                    new Vector2(rect.x + columnOffsets[i], rect.yMax),
                    color,
                    i == 4 || i == 7 ? 2f : 1f);
            }

            Text.Font = GameFont.Small;
        }

        private void DrawVisibleGroupSeparators(Rect outRect)
        {
            for (var i = 0; i < 2; i++)
            {
                var boundary = i == 0 ? 4 : 7;
                var x = outRect.x + columnOffsets[boundary] - scrollPosition.x;
                if (x <= outRect.x || x >= outRect.xMax - ScrollbarWidth)
                {
                    continue;
                }

                Widgets.DrawLine(
                    new Vector2(x, outRect.y),
                    new Vector2(x, outRect.yMax),
                    GroupLineColor,
                    2f);
            }
        }

        private void HandleColumnResize(Rect headerRect)
        {
            var current = Event.current;
            for (var i = 0; i < ColumnCount - 1; i++)
            {
                var boundaryX = headerRect.x + columnOffsets[i + 1];
                var handle = new Rect(boundaryX - 4f, headerRect.y, 8f, headerRect.height);
                TooltipHandler.TipRegion(
                    handle,
                    StatCompressionText.T("StatCompression_ResizeColumnTooltip"));
                if (current.type == EventType.MouseDown &&
                    current.button == 0 &&
                    handle.Contains(current.mousePosition))
                {
                    resizingColumn = i;
                    resizeStartMouseX = current.mousePosition.x;
                    Array.Copy(columnWidths, resizeStartWidths, ColumnCount);
                    current.Use();
                    break;
                }
            }

            if (resizingColumn < 0)
            {
                return;
            }

            if (current.type == EventType.MouseDrag && current.button == 0)
            {
                var rightColumn = resizingColumn + 1;
                var pairWidth = resizeStartWidths[resizingColumn] +
                                resizeStartWidths[rightColumn];
                var proposedLeft = resizeStartWidths[resizingColumn] +
                                   current.mousePosition.x -
                                   resizeStartMouseX;
                var left = Mathf.Clamp(
                    proposedLeft,
                    MinimumColumnWidths[resizingColumn],
                    pairWidth - MinimumColumnWidths[rightColumn]);
                columnWidths[resizingColumn] = left;
                columnWidths[rightColumn] = pairWidth - left;
                RebuildColumnOffsets();
                current.Use();
            }
            else if (current.rawType == EventType.MouseUp)
            {
                resizingColumn = -1;
            }
        }

        private void OpenMethodMenu(AdvancedRowState row)
        {
            var options = new List<FloatMenuOption>();
            var methods = new[]
            {
                CompressionMethod.FollowGlobal,
                CompressionMethod.Linear,
                CompressionMethod.Exponential,
                CompressionMethod.Logarithmic,
                CompressionMethod.SoftCap
            };
            for (var i = 0; i < methods.Length; i++)
            {
                var selectedMethod = methods[i];
                options.Add(new FloatMenuOption(
                    StatCompressionText.MethodLabel(selectedMethod),
                    () =>
                    {
                        row.Config.method = selectedMethod;
                        ApplyRowEdit(row, AdvancedConfigField.Method);
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenDirectionMenu(AdvancedRowState row)
        {
            var options = new List<FloatMenuOption>();
            var directions = new[]
            {
                StatCompressionDirection.HigherIsBetter,
                StatCompressionDirection.LowerIsBetter,
                StatCompressionDirection.LowerDirect
            };
            for (var i = 0; i < directions.Length; i++)
            {
                var selectedDirection = directions[i];
                options.Add(new FloatMenuOption(
                    StatCompressionText.DirectionShortLabel(selectedDirection) + ": " +
                    StatCompressionText.DirectionExplanation(selectedDirection),
                    () =>
                    {
                        row.Config.direction = selectedDirection;
                        ApplyRowEdit(
                            row,
                            AdvancedConfigField.Direction | AdvancedConfigField.Threshold);
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void ToggleVisibleSelection()
        {
            var select = filteredRows.Any(row =>
                !presetSelection.Contains(row.Config.defName));
            for (var i = 0; i < filteredRows.Count; i++)
            {
                if (select)
                {
                    presetSelection.Add(filteredRows[i].Config.defName);
                }
                else
                {
                    presetSelection.Remove(filteredRows[i].Config.defName);
                }
            }
        }

        private void SetSort(SortColumn column)
        {
            if (sortColumn == column)
            {
                sortAscending = !sortAscending;
            }
            else
            {
                sortColumn = column;
                sortAscending = true;
            }

            sortDirty = true;
        }

        private int CompareRows(AdvancedRowState left, AdvancedRowState right)
        {
            int comparison;
            switch (sortColumn)
            {
                case SortColumn.Type:
                    comparison = string.Compare(
                        left.TypeLabel,
                        right.TypeLabel,
                        StringComparison.CurrentCultureIgnoreCase);
                    break;
                case SortColumn.Enabled:
                    comparison = left.Config.enabled.CompareTo(right.Config.enabled);
                    break;
                case SortColumn.Label:
                    comparison = string.Compare(
                        left.Label,
                        right.Label,
                        StringComparison.CurrentCultureIgnoreCase);
                    break;
                case SortColumn.Method:
                    comparison = left.Config.method.CompareTo(right.Config.method);
                    break;
                case SortColumn.TScale:
                    comparison = showActualParameter
                        ? left.ActualParameter.CompareTo(right.ActualParameter)
                        : left.Config.tScale.CompareTo(right.Config.tScale);
                    break;
                case SortColumn.Baseline:
                    comparison = left.Config.baseline.CompareTo(right.Config.baseline);
                    break;
                case SortColumn.Threshold:
                    comparison = left.Config.thresholdFactor.CompareTo(right.Config.thresholdFactor);
                    break;
                case SortColumn.Direction:
                    comparison = left.Config.direction.CompareTo(right.Config.direction);
                    break;
                default:
                    comparison = string.Compare(
                        left.Config.defName,
                        right.Config.defName,
                        StringComparison.OrdinalIgnoreCase);
                    break;
            }

            if (comparison == 0)
            {
                comparison = string.Compare(
                    left.Config.defName,
                    right.Config.defName,
                    StringComparison.OrdinalIgnoreCase);
            }

            return sortAscending ? comparison : -comparison;
        }

        private void EnsureColumnWidths(float availableWidth)
        {
            if (columnWidthsInitialized)
            {
                return;
            }

            var defaultTotal = DefaultColumnWidths.Sum();
            var minimumTotal = MinimumColumnWidths.Sum();
            if (availableWidth <= minimumTotal)
            {
                Array.Copy(MinimumColumnWidths, columnWidths, ColumnCount);
            }
            else if (availableWidth < defaultTotal)
            {
                var scale = (availableWidth - minimumTotal) /
                            (defaultTotal - minimumTotal);
                for (var i = 0; i < ColumnCount; i++)
                {
                    columnWidths[i] = MinimumColumnWidths[i] +
                                      (DefaultColumnWidths[i] - MinimumColumnWidths[i]) * scale;
                }
            }
            else
            {
                Array.Copy(DefaultColumnWidths, columnWidths, ColumnCount);
                var extra = availableWidth - defaultTotal;
                columnWidths[1] += extra * 0.12f;
                columnWidths[2] += extra * 0.34f;
                columnWidths[3] += extra * 0.30f;
                columnWidths[9] += extra * 0.24f;
            }

            columnWidthsInitialized = true;
            RebuildColumnOffsets();
        }

        private void RebuildColumnOffsets()
        {
            columnOffsets[0] = 0f;
            for (var i = 0; i < ColumnCount; i++)
            {
                columnOffsets[i + 1] = columnOffsets[i] + columnWidths[i];
            }
        }

        private Rect Col(Rect row, int index)
        {
            return new Rect(
                row.x + columnOffsets[index] + 2f,
                row.y + 3f,
                columnWidths[index] - 4f,
                row.height - 6f);
        }

        private Rect GroupRect(Rect row, int firstColumn, int endColumn)
        {
            return new Rect(
                row.x + columnOffsets[firstColumn],
                row.y,
                columnOffsets[endColumn] - columnOffsets[firstColumn],
                row.height);
        }

    }
}

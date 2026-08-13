using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal readonly struct AdvancedTableInteraction
    {
        public AdvancedTableInteraction(
            bool selectionChanged,
            bool configChanged,
            StatCompressionStatConfig changedConfig)
        {
            SelectionChanged = selectionChanged;
            ConfigChanged = configChanged;
            ChangedConfig = changedConfig;
        }

        public bool SelectionChanged { get; }
        public bool ConfigChanged { get; }
        public StatCompressionStatConfig ChangedConfig { get; }
    }

    internal sealed class AdvancedTableComponent
    {
        private const float RowHeight = 30f;
        private const int ColumnCount = 9;

        private static readonly float[] DefaultColumnWidths =
        {
            34f, 36f, 160f, 120f, 88f, 72f, 86f, 86f, 110f
        };

        private static readonly float[] MinimumColumnWidths =
        {
            30f, 32f, 102f, 76f, 68f, 58f, 66f, 66f, 84f
        };

        private readonly StatCompressionSettings settings;
        private readonly List<AdvancedRowState> allRows = new List<AdvancedRowState>();
        private readonly List<AdvancedRowState> filteredRows = new List<AdvancedRowState>();
        private readonly Dictionary<string, AdvancedRowState> rowsByDefName =
            new Dictionary<string, AdvancedRowState>(StringComparer.Ordinal);
        private readonly HashSet<string> presetSelection =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly float[] columnWidths = (float[])DefaultColumnWidths.Clone();
        private readonly float[] columnOffsets = new float[ColumnCount + 1];
        private readonly float[] resizeStartWidths = new float[ColumnCount];
        private readonly string[] columnLabels;

        private object sourceToken;
        private int structureVersion = -1;
        private AdvancedDataSourceKind sourceKind;
        private bool hasData;
        private string searchText;
        private string selectedDefName;
        private SortColumn sortColumn = SortColumn.DefName;
        private bool sortAscending = true;
        private bool filterDirty = true;
        private bool sortDirty = true;
        private Vector2 scrollPosition;
        private bool columnWidthsInitialized;
        private int resizingColumn = -1;
        private float resizeStartMouseX;

        private enum SortColumn
        {
            Selection,
            Enabled,
            DefName,
            Label,
            Method,
            TScale,
            Baseline,
            Threshold,
            Direction
        }

        private sealed class AdvancedRowState
        {
            public StatCompressionStatConfig Config;
            public StatDef Stat;
            public string Label;
            public string SearchText;
            public string TScaleBuffer;
            public string BaselineBuffer;
            public string ThresholdBuffer;
            public string MethodLabel;
            public string DirectionLabel;
            public bool FixedDirection;
            public bool IsDamage;
            public bool IsHediffStage;
        }

        public AdvancedTableComponent(
            StatCompressionSettings settings,
            string focusDefName = null)
        {
            this.settings = settings;
            searchText = focusDefName ?? string.Empty;
            selectedDefName = focusDefName;
            columnLabels = new[]
            {
                StatCompressionText.T("StatCompression_Column_Select"),
                StatCompressionText.T("StatCompression_Column_On"),
                StatCompressionText.T("StatCompression_Column_DefName"),
                StatCompressionText.T("StatCompression_Column_Label"),
                StatCompressionText.T("StatCompression_Column_Method"),
                StatCompressionText.T("StatCompression_Column_TScale"),
                StatCompressionText.T("StatCompression_Column_Baseline"),
                StatCompressionText.T("StatCompression_Column_Threshold"),
                StatCompressionText.T("StatCompression_Column_Direction")
            };
        }

        public StatCompressionStatConfig SelectedConfig
        {
            get
            {
                AdvancedRowState row;
                return !selectedDefName.NullOrEmpty() &&
                       rowsByDefName.TryGetValue(selectedDefName, out row)
                    ? row.Config
                    : null;
            }
        }

        public void SetData(AdvancedDataSet data)
        {
            if (ReferenceEquals(sourceToken, data.SourceToken) &&
                structureVersion == data.StructureVersion)
            {
                return;
            }

            var kindChanged = hasData && sourceKind != data.Kind;
            sourceToken = data.SourceToken;
            structureVersion = data.StructureVersion;
            sourceKind = data.Kind;
            hasData = true;

            allRows.Clear();
            rowsByDefName.Clear();
            for (var i = 0; i < data.Configs.Count; i++)
            {
                var row = BuildRowState(data.Configs[i]);
                allRows.Add(row);
                rowsByDefName.Add(row.Config.defName, row);
            }

            if (kindChanged)
            {
                searchText = string.Empty;
                selectedDefName = null;
                scrollPosition = Vector2.zero;
            }
            else if (!selectedDefName.NullOrEmpty() &&
                     !rowsByDefName.ContainsKey(selectedDefName))
            {
                selectedDefName = null;
            }

            filterDirty = true;
            sortDirty = true;
        }

        public void ApplyUpdate(AdvancedConfigUpdate update)
        {
            if (update.Config == null || update.Config.defName.NullOrEmpty())
            {
                return;
            }

            AdvancedRowState row;
            if (!rowsByDefName.TryGetValue(update.Config.defName, out row))
            {
                return;
            }

            row.Config = update.Config;
            ApplyRowEdit(row, update.Fields, true);
        }

        public void DrawSearch(Rect rect)
        {
            Widgets.Label(
                new Rect(rect.x, rect.y, 64f, rect.height),
                StatCompressionText.T("StatCompression_Search"));
            var nextSearch = Widgets.TextField(
                new Rect(rect.x + 68f, rect.y, rect.width - 68f, rect.height),
                searchText ?? string.Empty);
            if (nextSearch != searchText)
            {
                searchText = nextSearch;
                filterDirty = true;
            }
        }

        public AdvancedTableInteraction DrawTable(Rect rect)
        {
            UpdateDerivedRows();

            var headerRect = new Rect(rect.x, rect.y, rect.width - 16f, RowHeight);
            EnsureColumnWidths(headerRect.width);
            HandleColumnResize(headerRect);
            DrawHeader(headerRect);

            if (selectedDefName.NullOrEmpty() && filteredRows.Count > 0)
            {
                selectedDefName = filteredRows[0].Config.defName;
            }

            var outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.yMax - headerRect.yMax);
            var viewRect = new Rect(0f, 0f, headerRect.width, filteredRows.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            var interaction = DrawVisibleRows(outRect, viewRect);
            Widgets.EndScrollView();
            return interaction;
        }

        public IReadOnlyList<StatCompressionStatConfig> GetPresetSelection()
        {
            var result = new List<StatCompressionStatConfig>(presetSelection.Count);
            for (var i = 0; i < allRows.Count; i++)
            {
                var config = allRows[i].Config;
                if (presetSelection.Contains(config.defName))
                {
                    result.Add(config);
                }
            }

            return result;
        }

        public void ClearPresetSelection()
        {
            presetSelection.Clear();
        }

        private AdvancedRowState BuildRowState(StatCompressionStatConfig config)
        {
            var isDamage = SpecialCompressionConfigs.IsDamage(config.defName);
            var isHediffStage = SpecialCompressionConfigs.IsHediffStage(config.defName);
            var fixedDirection = isDamage || isHediffStage;
            if (fixedDirection)
            {
                config.direction = isDamage
                    ? StatCompressionDirection.HigherIsBetter
                    : SpecialCompressionConfigs.DirectionForHediffStage(config.defName);
            }

            var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
            var label = LabelFor(config, stat);
            var category = stat?.category?.defName ?? string.Empty;
            return new AdvancedRowState
            {
                Config = config,
                Stat = stat,
                Label = label,
                SearchText = ((config.defName ?? string.Empty) + "\n" + label + "\n" + category)
                    .ToLowerInvariant(),
                TScaleBuffer = config.tScale.ToString(),
                BaselineBuffer = config.baseline.ToString(),
                ThresholdBuffer = (config.thresholdFactor * 100f).ToString(),
                MethodLabel = StatCompressionText.MethodShortLabel(config.method),
                DirectionLabel = StatCompressionText.DirectionShortLabel(config.direction),
                FixedDirection = fixedDirection,
                IsDamage = isDamage,
                IsHediffStage = isHediffStage
            };
        }

        private void RefreshMetadata(AdvancedRowState row)
        {
            row.Stat = DefDatabase<StatDef>.GetNamedSilentFail(row.Config.defName);
            row.Label = LabelFor(row.Config, row.Stat);
            var category = row.Stat?.category?.defName ?? string.Empty;
            row.SearchText = ((row.Config.defName ?? string.Empty) + "\n" + row.Label + "\n" + category)
                .ToLowerInvariant();
        }

        private void UpdateDerivedRows()
        {
            if (filterDirty)
            {
                filteredRows.Clear();
                var needle = (searchText ?? string.Empty).ToLowerInvariant();
                for (var i = 0; i < allRows.Count; i++)
                {
                    if (needle.Length == 0 || allRows[i].SearchText.Contains(needle))
                    {
                        filteredRows.Add(allRows[i]);
                    }
                }

                filterDirty = false;
                sortDirty = true;
            }

            if (sortDirty)
            {
                filteredRows.Sort(CompareRows);
                sortDirty = false;
            }
        }

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

        private AdvancedTableInteraction DrawRow(Rect rect, AdvancedRowState row, int rowIndex)
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
            Widgets.Checkbox(Col(rect, 0).position + new Vector2(2f, 3f), ref selectedForPreset);
            if (selectedForPreset)
            {
                presetSelection.Add(config.defName);
            }
            else
            {
                presetSelection.Remove(config.defName);
            }

            var changedFields = AdvancedConfigField.None;
            var enabled = config.enabled;
            Widgets.Checkbox(Col(rect, 1).position + new Vector2(2f, 3f), ref enabled);
            if (enabled != config.enabled)
            {
                config.enabled = enabled;
                changedFields |= AdvancedConfigField.Enabled;
            }

            var defRect = Col(rect, 2);
            var labelRect = Col(rect, 3);
            Text.Font = GameFont.Tiny;
            Widgets.Label(defRect, config.defName);
            Widgets.Label(labelRect, row.Label);
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(Col(rect, 4), row.MethodLabel))
            {
                OpenMethodMenu(row);
            }

            var oldTScale = config.tScale;
            Widgets.TextFieldNumeric(
                Col(rect, 5),
                ref config.tScale,
                ref row.TScaleBuffer,
                0.0001f,
                float.MaxValue);
            if (!NearlyEqual(oldTScale, config.tScale))
            {
                changedFields |= AdvancedConfigField.TScale;
            }
            TooltipHandler.TipRegion(Col(rect, 5), StatCompressionText.T("StatCompression_TScaleTooltip"));

            var oldBaseline = config.baseline;
            Widgets.TextFieldNumeric(
                Col(rect, 6),
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
                Col(rect, 7),
                ref thresholdPercent,
                ref row.ThresholdBuffer,
                thresholdMinimum,
                float.MaxValue);
            if (!NearlyEqual(oldThreshold, thresholdPercent))
            {
                config.thresholdFactor = thresholdPercent / 100f;
                changedFields |= AdvancedConfigField.Threshold;
            }

            var directionRect = Col(rect, 8);
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
        }

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.18f, 1f));
            Text.Font = GameFont.Tiny;
            for (var i = 0; i < ColumnCount; i++)
            {
                var cell = Col(rect, i);
                if (Mouse.IsOver(cell))
                {
                    Widgets.DrawHighlight(cell);
                }

                var column = (SortColumn)i;
                var label = columnLabels[i];
                if (i > 0 && sortColumn == column)
                {
                    label += sortAscending ? " ^" : " v";
                }

                Widgets.Label(cell.ContractedBy(2f), label);
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
                Widgets.DrawLineVertical(rect.x + columnOffsets[i], rect.y, rect.height);
            }

            Text.Font = GameFont.Small;
        }

        private void HandleColumnResize(Rect headerRect)
        {
            var current = Event.current;
            for (var i = 0; i < ColumnCount - 1; i++)
            {
                var boundaryX = headerRect.x + columnOffsets[i + 1];
                var handle = new Rect(boundaryX - 4f, headerRect.y, 8f, headerRect.height);
                Widgets.DrawLineVertical(boundaryX, headerRect.y, headerRect.height);
                TooltipHandler.TipRegion(handle, StatCompressionText.T("StatCompression_ResizeColumnTooltip"));
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
                var pairWidth = resizeStartWidths[resizingColumn] + resizeStartWidths[rightColumn];
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
            var select = filteredRows.Any(row => !presetSelection.Contains(row.Config.defName));
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
                    comparison = left.Config.tScale.CompareTo(right.Config.tScale);
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
            var targetWidth = Math.Max(minimumTotal, availableWidth);
            var flexibleDefault = defaultTotal - minimumTotal;
            var flexibleTarget = targetWidth - minimumTotal;
            var scale = flexibleDefault <= 0f
                ? 0f
                : Mathf.Clamp01(flexibleTarget / flexibleDefault);
            for (var i = 0; i < ColumnCount; i++)
            {
                columnWidths[i] = MinimumColumnWidths[i] +
                                  (DefaultColumnWidths[i] - MinimumColumnWidths[i]) * scale;
            }

            columnWidths[ColumnCount - 1] += availableWidth - columnWidths.Sum();
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

        private string BuildTooltip(AdvancedRowState row)
        {
            var config = row.Config;
            var actualThreshold = StatCompressionRuntime.GetActualThresholdFactor(
                config.method,
                settings.thresholdFactor,
                config.thresholdFactor);
            return StatCompressionText.T("StatCompression_Tooltip_Baseline", config.baseline) +
                   "\n" + StatCompressionText.T(
                       "StatCompression_Tooltip_Threshold",
                       (actualThreshold * 100f).ToString("0.###")) +
                   "\n" + StatCompressionText.T(
                       "StatCompression_Tooltip_Method",
                       StatCompressionText.MethodLabel(config.method)) +
                   "\n" + StatCompressionText.T("StatCompression_Tooltip_TScale", config.tScale) +
                   "\n" + StatCompressionText.T(
                       "StatCompression_Tooltip_Direction",
                       StatCompressionText.DirectionShortLabel(config.direction)) +
                   (SpecialCompressionConfigs.IsSpecial(config.defName)
                       ? "\n" + StatCompressionText.T("StatCompression_Tooltip_SpecialModule") +
                         SpecialTooltip(config.defName)
                       : row.Stat == null
                           ? string.Empty
                           : "\n" + StatCompressionText.T(
                               "StatCompression_Tooltip_Category",
                               row.Stat.category?.defName));
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
                : stat?.LabelCap.ToString() ?? string.Empty;
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= 0.000001f;
        }
    }
}

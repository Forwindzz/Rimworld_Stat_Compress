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
        private const float GroupHeaderHeight = 26f;
        private const float ColumnHeaderHeight = 30f;
        private const float RowHeight = 30f;
        private const float ScrollbarWidth = 16f;
        private const int ColumnCount = 10;

        private static readonly float[] DefaultColumnWidths =
        {
            44f, 110f, 190f, 150f, 72f, 96f, 112f, 90f, 96f, 124f
        };

        private static readonly float[] MinimumColumnWidths =
        {
            38f, 76f, 116f, 96f, 64f, 72f, 84f, 68f, 76f, 94f
        };

        private static readonly Color GroupLineColor =
            new Color(0.85f, 0.85f, 0.85f, 0.7f);
        private static readonly Color ColumnLineColor =
            new Color(0.48f, 0.48f, 0.48f, 0.45f);

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
        private bool showActualParameter;
        private bool hasGlobalInput;
        private GlobalCompressionInput globalInput;

        private enum SortColumn
        {
            Selection,
            Type,
            DefName,
            Label,
            Enabled,
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
            public string TypeLabel;
            public string SearchText;
            public string TScaleBuffer;
            public string BaselineBuffer;
            public string ThresholdBuffer;
            public string MethodLabel;
            public string DirectionLabel;
            public float ActualParameter;
            public string ActualParameterText;
            public string ActualParameterTooltip;
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
            globalInput = new GlobalCompressionInput(settings);
            hasGlobalInput = true;
            columnLabels = new[]
            {
                StatCompressionText.T("StatCompression_Column_Select"),
                StatCompressionText.T("StatCompression_Column_Type"),
                StatCompressionText.T("StatCompression_Column_DefName"),
                StatCompressionText.T("StatCompression_Column_Label"),
                StatCompressionText.T("StatCompression_Column_EnableCompression"),
                StatCompressionText.T("StatCompression_Column_CompressionMethod"),
                StatCompressionText.T("StatCompression_Column_TScaleLong"),
                StatCompressionText.T("StatCompression_Column_Baseline"),
                StatCompressionText.T("StatCompression_Column_ThresholdPercent"),
                StatCompressionText.T("StatCompression_Column_Normalization")
            };
        }

        public bool ShowActualParameter
        {
            get => showActualParameter;
            set
            {
                if (showActualParameter == value)
                {
                    return;
                }

                showActualParameter = value;
                if (sortColumn == SortColumn.TScale)
                {
                    sortDirty = true;
                }
            }
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

        public void SetGlobalInput(GlobalCompressionInput input)
        {
            if (hasGlobalInput && GlobalInputEquals(globalInput, input))
            {
                return;
            }

            globalInput = input;
            hasGlobalInput = true;
            for (var i = 0; i < allRows.Count; i++)
            {
                RefreshActualParameter(allRows[i]);
            }

            if (sortColumn == SortColumn.TScale && showActualParameter)
            {
                sortDirty = true;
            }
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

            var headerViewport = new Rect(
                rect.x,
                rect.y,
                rect.width - ScrollbarWidth,
                GroupHeaderHeight + ColumnHeaderHeight);
            EnsureColumnWidths(headerViewport.width);
            var contentWidth = columnOffsets[ColumnCount];

            GUI.BeginGroup(headerViewport);
            var groupedHeaderRect = new Rect(
                -scrollPosition.x,
                0f,
                contentWidth,
                headerViewport.height);
            HandleColumnResize(groupedHeaderRect);
            DrawHeader(groupedHeaderRect);
            GUI.EndGroup();

            if (selectedDefName.NullOrEmpty() && filteredRows.Count > 0)
            {
                selectedDefName = filteredRows[0].Config.defName;
            }

            var outRect = new Rect(
                rect.x,
                headerViewport.yMax,
                rect.width,
                rect.yMax - headerViewport.yMax);
            var viewRect = new Rect(
                0f,
                0f,
                contentWidth,
                filteredRows.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            var interaction = DrawVisibleRows(outRect, viewRect);
            Widgets.EndScrollView();
            DrawVisibleGroupSeparators(outRect);
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
            var row = new AdvancedRowState
            {
                Config = config,
                Stat = stat,
                Label = LabelFor(config, stat),
                TypeLabel = TypeLabelFor(config, stat),
                TScaleBuffer = config.tScale.ToString(),
                BaselineBuffer = config.baseline.ToString(),
                ThresholdBuffer = (config.thresholdFactor * 100f).ToString(),
                MethodLabel = StatCompressionText.MethodShortLabel(config.method),
                DirectionLabel = StatCompressionText.DirectionShortLabel(config.direction),
                FixedDirection = fixedDirection,
                IsDamage = isDamage,
                IsHediffStage = isHediffStage
            };
            RebuildSearchText(row);
            RefreshActualParameter(row);
            return row;
        }

        private void RefreshMetadata(AdvancedRowState row)
        {
            row.Stat = DefDatabase<StatDef>.GetNamedSilentFail(row.Config.defName);
            row.Label = LabelFor(row.Config, row.Stat);
            row.TypeLabel = TypeLabelFor(row.Config, row.Stat);
            RebuildSearchText(row);
        }

        private static void RebuildSearchText(AdvancedRowState row)
        {
            row.SearchText = ((row.Config.defName ?? string.Empty) + "\n" +
                              row.Label + "\n" +
                              row.TypeLabel).ToLowerInvariant();
        }

        private void RefreshActualParameter(AdvancedRowState row)
        {
            var config = row.Config;
            var actualMethod = StatCompressionRuntime.ResolveMethod(
                config.method,
                globalInput.Method);
            var baseParameter = config.method == CompressionMethod.FollowGlobal
                ? globalInput.Parameter
                : StatCompressionRuntime.DefaultParameter(actualMethod);
            row.ActualParameter = StatCompressionRuntime.GetActualParameter(
                config.method,
                globalInput.Method,
                globalInput.Parameter,
                config.tScale);
            row.ActualParameterText = row.ActualParameter.ToString("0.###");
            var source = config.method == CompressionMethod.FollowGlobal
                ? StatCompressionText.T("StatCompression_ActualT_GlobalSource")
                : StatCompressionText.T("StatCompression_ActualT_DefaultSource");
            row.ActualParameterTooltip = StatCompressionText.T(
                actualMethod == CompressionMethod.Logarithmic
                    ? "StatCompression_ActualT_MultiplyTooltip"
                    : "StatCompression_ActualT_DivideTooltip",
                source,
                baseParameter.ToString("0.###"),
                config.tScale.ToString("0.###"),
                row.ActualParameterText);
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
            Text.Font = GameFont.Tiny;
            DrawSingleLineText(typeRect, row.TypeLabel);
            DrawSingleLineText(defRect, config.defName);
            DrawSingleLineText(labelRect, row.Label);
            Text.Font = GameFont.Small;

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
                   (SpecialCompressionConfigs.IsSpecial(config.defName)
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
                : stat?.LabelCap.ToString() ?? string.Empty;
        }

        private static string TypeLabelFor(StatCompressionStatConfig config, StatDef stat)
        {
            if (SpecialCompressionConfigs.IsSpecial(config.defName))
            {
                return StatCompressionText.T("StatCompression_Type_SpecialModule");
            }

            var category = stat?.category;
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

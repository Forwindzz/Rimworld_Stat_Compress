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

    internal sealed partial class AdvancedTableComponent
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
        private static readonly Color MissingStatColor =
            new Color(1f, 0.72f, 0.38f, 1f);

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
            public bool MissingStat;
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
            var missingStat = stat == null && !SpecialCompressionConfigs.IsSpecial(config.defName);
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
                IsHediffStage = isHediffStage,
                MissingStat = missingStat
            };
            RebuildSearchText(row);
            RefreshActualParameter(row);
            return row;
        }

        private void RefreshMetadata(AdvancedRowState row)
        {
            row.Stat = DefDatabase<StatDef>.GetNamedSilentFail(row.Config.defName);
            row.MissingStat = row.Stat == null &&
                              !SpecialCompressionConfigs.IsSpecial(row.Config.defName);
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

    }
}

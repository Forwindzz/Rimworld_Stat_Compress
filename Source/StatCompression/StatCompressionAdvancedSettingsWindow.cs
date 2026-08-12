using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed class StatCompressionAdvancedSettingsWindow : Window
    {
        private const float RowHeight = 30f;
        private const float SplitGap = 12f;
        private const int ColumnCount = 8;

        private static readonly float[] DefaultColumnWidths =
        {
            36f, 184f, 132f, 90f, 78f, 94f, 94f, 118f
        };

        private static readonly float[] MinimumColumnWidths =
        {
            32f, 112f, 82f, 70f, 62f, 72f, 72f, 92f
        };

        private readonly StatCompressionSettings settings;
        private readonly Dictionary<string, string> tScaleBuffers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> baselineBuffers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> thresholdBuffers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly float[] columnWidths = (float[])DefaultColumnWidths.Clone();
        private readonly float[] resizeStartWidths = new float[ColumnCount];
        private readonly float[] higherPreviewPercents = { 50f, 100f, 150f, 200f, 500f, 5000f, 100000f };
        private readonly float[] lowerPreviewPercents = { 150f, 100f, 75f, 40f, 10f, 1f, 0.1f };
        private readonly string[] higherPreviewBuffers = new string[7];
        private readonly string[] lowerPreviewBuffers = new string[7];

        private Vector2 scrollPosition;
        private string searchText = string.Empty;
        private string selectedDefName;
        private SortColumn sortColumn = SortColumn.DefName;
        private bool sortAscending = true;
        private bool columnWidthsInitialized;
        private int resizingColumn = -1;
        private float resizeStartMouseX;

        private enum SortColumn
        {
            Enabled,
            DefName,
            Label,
            Method,
            TScale,
            Baseline,
            Threshold,
            Direction
        }

        public StatCompressionAdvancedSettingsWindow(StatCompressionSettings settings)
        {
            this.settings = settings;
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            optionalTitle = StatCompressionText.T("StatCompression_AdvancedTitle");
        }

        public override Vector2 InitialSize => new Vector2(1280f, 780f);

        public override void DoWindowContents(Rect inRect)
        {
            settings.EnsureStatConfigs();

            var helpRect = new Rect(inRect.x, inRect.y, inRect.width, 78f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(helpRect.x, helpRect.y, helpRect.width, 26f), StatCompressionText.T("StatCompression_AdvancedHelp"));
            Widgets.Label(new Rect(helpRect.x, helpRect.y + 24f, helpRect.width, 24f), StatCompressionText.T("StatCompression_AdvancedFormulaHelp"));
            Widgets.Label(new Rect(helpRect.x, helpRect.y + 48f, helpRect.width, 24f), StatCompressionText.T("StatCompression_DirectionHelp"));
            Text.Font = GameFont.Small;

            var contentTop = helpRect.yMax + 8f;
            var previewWidth = Mathf.Clamp(inRect.width * 0.34f, 360f, 440f);
            var leftRect = new Rect(inRect.x, contentTop, inRect.width - previewWidth - SplitGap, inRect.yMax - contentTop);
            var previewRect = new Rect(leftRect.xMax + SplitGap, contentTop, previewWidth, inRect.yMax - contentTop);
            DrawTable(leftRect);
            DrawSelectedPreview(previewRect);
        }

        public override void PostClose()
        {
            settings.NormalizeParameters();
            settings.RebuildLookup();
            base.PostClose();
        }

        private void DrawTable(Rect rect)
        {
            var searchRect = new Rect(rect.x, rect.y, rect.width, 30f);
            Widgets.Label(new Rect(searchRect.x, searchRect.y, 90f, searchRect.height), StatCompressionText.T("StatCompression_Search"));
            searchText = Widgets.TextField(
                new Rect(searchRect.x + 96f, searchRect.y, searchRect.width - 96f, searchRect.height),
                searchText ?? string.Empty);

            var headerRect = new Rect(rect.x, searchRect.yMax + 10f, rect.width - 16f, RowHeight);
            EnsureColumnWidths(headerRect.width);
            HandleColumnResize(headerRect);
            DrawHeader(headerRect);

            var configs = settings.StatConfigs.Where(MatchesSearch).ToList();
            SortConfigs(configs);
            if (selectedDefName.NullOrEmpty() && configs.Count > 0)
            {
                selectedDefName = configs[0].defName;
            }

            var outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.yMax - headerRect.yMax);
            var viewRect = new Rect(0f, 0f, headerRect.width, configs.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            for (var i = 0; i < configs.Count; i++)
            {
                DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), configs[i], i);
            }

            Widgets.EndScrollView();
        }

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.18f, 1f));
            var labels = new[]
            {
                StatCompressionText.T("StatCompression_Column_On"),
                StatCompressionText.T("StatCompression_Column_DefName"),
                StatCompressionText.T("StatCompression_Column_Label"),
                StatCompressionText.T("StatCompression_Column_Method"),
                StatCompressionText.T("StatCompression_Column_TScale"),
                StatCompressionText.T("StatCompression_Column_Baseline"),
                StatCompressionText.T("StatCompression_Column_Threshold"),
                StatCompressionText.T("StatCompression_Column_Direction")
            };

            Text.Font = GameFont.Tiny;
            for (var i = 0; i < ColumnCount; i++)
            {
                var cell = Col(rect, i);
                if (Mouse.IsOver(cell))
                {
                    Widgets.DrawHighlight(cell);
                }

                var column = (SortColumn)i;
                var label = labels[i];
                if (sortColumn == column)
                {
                    label += sortAscending ? " ^" : " v";
                }

                Widgets.Label(cell.ContractedBy(2f), label);
                if (Widgets.ButtonInvisible(cell, false))
                {
                    SetSort(column);
                }
            }

            var boundaryX = rect.x;
            for (var i = 0; i < ColumnCount - 1; i++)
            {
                boundaryX += columnWidths[i];
                Widgets.DrawLineVertical(boundaryX, rect.y, rect.height);
            }

            Text.Font = GameFont.Small;
        }

        private void HandleColumnResize(Rect headerRect)
        {
            var current = Event.current;
            var boundaryX = headerRect.x;
            for (var i = 0; i < ColumnCount - 1; i++)
            {
                boundaryX += columnWidths[i];
                var handle = new Rect(boundaryX - 4f, headerRect.y, 8f, headerRect.height);
                Widgets.DrawLineVertical(boundaryX, headerRect.y, headerRect.height);
                TooltipHandler.TipRegion(handle, StatCompressionText.T("StatCompression_ResizeColumnTooltip"));
                if (current.type == EventType.MouseDown && current.button == 0 && handle.Contains(current.mousePosition))
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
                var proposedLeft = resizeStartWidths[resizingColumn] + current.mousePosition.x - resizeStartMouseX;
                var left = Mathf.Clamp(
                    proposedLeft,
                    MinimumColumnWidths[resizingColumn],
                    pairWidth - MinimumColumnWidths[rightColumn]);
                columnWidths[resizingColumn] = left;
                columnWidths[rightColumn] = pairWidth - left;
                current.Use();
            }
            else if (current.rawType == EventType.MouseUp)
            {
                resizingColumn = -1;
            }
        }

        private void DrawRow(Rect rect, StatCompressionStatConfig config, int rowIndex)
        {
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

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rect.Contains(Event.current.mousePosition))
            {
                selectedDefName = config.defName;
            }

            var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
            var enabled = config.enabled;
            Widgets.Checkbox(Col(rect, 0).position + new Vector2(2f, 3f), ref enabled);
            config.enabled = enabled;

            Text.Font = GameFont.Tiny;
            Widgets.Label(Col(rect, 1), config.defName);
            Widgets.Label(Col(rect, 2), stat == null ? string.Empty : stat.LabelCap.ToString());
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(Col(rect, 3), StatCompressionText.MethodShortLabel(config.method)))
            {
                OpenMethodMenu(config);
            }

            var tScaleBuffer = GetBuffer(tScaleBuffers, config.defName, "ts", config.tScale);
            Widgets.TextFieldNumeric(Col(rect, 4), ref config.tScale, ref tScaleBuffer, 0.0001f, float.MaxValue);
            tScaleBuffers[BufferKey(config.defName, "ts")] = tScaleBuffer;
            TooltipHandler.TipRegion(Col(rect, 4), StatCompressionText.T("StatCompression_TScaleTooltip"));

            var baselineBuffer = GetBuffer(baselineBuffers, config.defName, "b", config.baseline);
            Widgets.TextFieldNumeric(Col(rect, 5), ref config.baseline, ref baselineBuffer, 0.000001f, float.MaxValue);
            baselineBuffers[BufferKey(config.defName, "b")] = baselineBuffer;

            var thresholdPercent = config.thresholdFactor * 100f;
            var thresholdBuffer = GetBuffer(thresholdBuffers, config.defName, "th", thresholdPercent);
            Widgets.TextFieldNumeric(Col(rect, 6), ref thresholdPercent, ref thresholdBuffer, 0.0001f, float.MaxValue);
            config.thresholdFactor = Math.Max(0.0001f, thresholdPercent / 100f);
            thresholdBuffers[BufferKey(config.defName, "th")] = thresholdBuffer;

            if (Widgets.ButtonText(Col(rect, 7), StatCompressionText.DirectionShortLabel(config.direction)))
            {
                config.direction = config.direction == StatCompressionDirection.HigherIsBetter
                    ? StatCompressionDirection.LowerIsBetter
                    : StatCompressionDirection.HigherIsBetter;
            }
            TooltipHandler.TipRegion(Col(rect, 7), StatCompressionText.T("StatCompression_DirectionTooltip"));
            TooltipHandler.TipRegion(Col(rect, 1), Tooltip(stat, config));
            TooltipHandler.TipRegion(Col(rect, 2), Tooltip(stat, config));
            StatCompressionSettings.NormalizeConfig(config);
        }

        private void DrawSelectedPreview(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(10f);
            var config = selectedDefName.NullOrEmpty()
                ? null
                : settings.StatConfigs.FirstOrDefault(item => item.defName == selectedDefName);
            if (config == null)
            {
                Widgets.Label(inner, StatCompressionText.T("StatCompression_SelectStatForPreview"));
                return;
            }

            var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 30f),
                stat == null ? config.defName : stat.LabelCap.ToString());
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, inner.y + 30f, inner.width, 22f), config.defName);
            Text.Font = GameFont.Small;

            var actualParameter = StatCompressionRuntime.GetActualParameter(
                config.method,
                settings.method,
                settings.parameter,
                config.tScale);
            var thresholdValue = config.direction == StatCompressionDirection.HigherIsBetter
                ? config.baseline * config.thresholdFactor
                : config.baseline / config.thresholdFactor;
            var summaryY = inner.y + 56f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(inner.x, summaryY, inner.width, 42f),
                StatCompressionText.T(
                    "StatCompression_AdvancedPreviewSummary",
                    StatCompressionText.DirectionShortLabel(config.direction),
                    FormatStatValue(stat, config.baseline),
                    FormatStatValue(stat, thresholdValue)));

            var formula = BuildFormula(config, actualParameter);
            Widgets.Label(new Rect(inner.x, summaryY + 44f, inner.width, 142f), formula);
            Text.Font = GameFont.Small;

            var valuesY = summaryY + 190f;
            Widgets.DrawLineHorizontal(inner.x, valuesY, inner.width);
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(inner.x, valuesY + 4f, inner.width, 22f),
                StatCompressionText.T("StatCompression_AdvancedPreviewValues"));

            var percents = config.direction == StatCompressionDirection.HigherIsBetter
                ? higherPreviewPercents
                : lowerPreviewPercents;
            var buffers = config.direction == StatCompressionDirection.HigherIsBetter
                ? higherPreviewBuffers
                : lowerPreviewBuffers;
            var rowY = valuesY + 30f;
            for (var i = 0; i < percents.Length; i++)
            {
                DrawPreviewValueRow(new Rect(inner.x, rowY + i * 42f, inner.width, 38f), stat, config, percents, buffers, i);
            }

            Text.Font = GameFont.Small;
        }

        private void DrawPreviewValueRow(
            Rect rect,
            StatDef stat,
            StatCompressionStatConfig config,
            float[] percents,
            string[] buffers,
            int index)
        {
            if (buffers[index] == null)
            {
                buffers[index] = percents[index].ToString("0.###");
            }

            var inputRect = new Rect(rect.x, rect.y, 76f, 26f);
            Widgets.TextFieldNumeric(inputRect, ref percents[index], ref buffers[index], 0.000001f, float.MaxValue);
            Widgets.Label(new Rect(inputRect.xMax + 4f, rect.y + 2f, 18f, 24f), "%");

            var original = config.baseline * (percents[index] / 100f);
            var final = StatCompressionRuntime.ComputePreviewValue(settings, config, original);
            var mappedPercent = config.baseline == 0f ? 0f : final / config.baseline * 100f;
            Widgets.Label(
                new Rect(inputRect.xMax + 28f, rect.y + 2f, rect.width - inputRect.width - 28f, 18f),
                "-> " + mappedPercent.ToString("0.###") + "%");
            Widgets.Label(
                new Rect(rect.x, rect.y + 20f, rect.width, 18f),
                FormatStatValue(stat, original) + " -> " + FormatStatValue(stat, final));
        }

        private string BuildFormula(StatCompressionStatConfig config, float actualParameter)
        {
            var expression = CompressionExpression(config.method);
            var key = config.direction == StatCompressionDirection.HigherIsBetter
                ? "StatCompression_AdvancedPreviewFormulaHigher"
                : "StatCompression_AdvancedPreviewFormulaLower";
            return StatCompressionText.T(key, expression, actualParameter.ToString("0.###"));
        }

        private static string CompressionExpression(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return "e * t";
                case CompressionMethod.Exponential:
                    return "(e + 1)^t - 1";
                case CompressionMethod.Logarithmic:
                    return "ln(1 + ln(t) * e) / ln(t)";
                case CompressionMethod.SoftCap:
                    return "t * e / (e + t)";
                default:
                    return "e";
            }
        }

        private static string FormatStatValue(StatDef stat, float value)
        {
            return stat == null
                ? value.ToString("0.###")
                : stat.ValueToString(value, stat.toStringNumberSense, true);
        }

        private void OpenMethodMenu(StatCompressionStatConfig config)
        {
            var options = new List<FloatMenuOption>();
            var methods = new[]
            {
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
                        config.method = selectedMethod;
                        StatCompressionSettings.NormalizeConfig(config);
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void SetSort(SortColumn column)
        {
            if (sortColumn == column)
            {
                sortAscending = !sortAscending;
                return;
            }

            sortColumn = column;
            sortAscending = true;
        }

        private void SortConfigs(List<StatCompressionStatConfig> configs)
        {
            configs.Sort((left, right) =>
            {
                var comparison = CompareConfig(left, right, sortColumn);
                if (comparison == 0)
                {
                    comparison = string.Compare(left.defName, right.defName, StringComparison.OrdinalIgnoreCase);
                }

                return sortAscending ? comparison : -comparison;
            });
        }

        private static int CompareConfig(StatCompressionStatConfig left, StatCompressionStatConfig right, SortColumn column)
        {
            switch (column)
            {
                case SortColumn.Enabled:
                    return left.enabled.CompareTo(right.enabled);
                case SortColumn.Label:
                    return string.Compare(LabelFor(left), LabelFor(right), StringComparison.CurrentCultureIgnoreCase);
                case SortColumn.Method:
                    return left.method.CompareTo(right.method);
                case SortColumn.TScale:
                    return left.tScale.CompareTo(right.tScale);
                case SortColumn.Baseline:
                    return left.baseline.CompareTo(right.baseline);
                case SortColumn.Threshold:
                    return left.thresholdFactor.CompareTo(right.thresholdFactor);
                case SortColumn.Direction:
                    return left.direction.CompareTo(right.direction);
                default:
                    return string.Compare(left.defName, right.defName, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string LabelFor(StatCompressionStatConfig config)
        {
            var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
            return stat?.LabelCap.ToString() ?? string.Empty;
        }

        private bool MatchesSearch(StatCompressionStatConfig config)
        {
            if (searchText.NullOrEmpty())
            {
                return true;
            }

            var needle = searchText.ToLowerInvariant();
            if (!config.defName.NullOrEmpty() && config.defName.ToLowerInvariant().Contains(needle))
            {
                return true;
            }

            var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
            return stat != null &&
                   ((stat.label != null && stat.label.ToLowerInvariant().Contains(needle)) ||
                    (stat.category != null && stat.category.defName.ToLowerInvariant().Contains(needle)));
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
            var scale = flexibleDefault <= 0f ? 0f : Mathf.Clamp01(flexibleTarget / flexibleDefault);
            for (var i = 0; i < ColumnCount; i++)
            {
                columnWidths[i] = MinimumColumnWidths[i] + (DefaultColumnWidths[i] - MinimumColumnWidths[i]) * scale;
            }

            var currentTotal = columnWidths.Sum();
            columnWidths[ColumnCount - 1] += availableWidth - currentTotal;
            columnWidthsInitialized = true;
        }

        private Rect Col(Rect row, int index)
        {
            var x = row.x;
            for (var i = 0; i < index; i++)
            {
                x += columnWidths[i];
            }

            return new Rect(x + 2f, row.y + 3f, columnWidths[index] - 4f, row.height - 6f);
        }

        private static string GetBuffer(Dictionary<string, string> buffers, string defName, string field, float value)
        {
            var key = BufferKey(defName, field);
            if (!buffers.TryGetValue(key, out var buffer))
            {
                buffer = value.ToString();
                buffers[key] = buffer;
            }

            return buffer;
        }

        private static string BufferKey(string defName, string field)
        {
            return defName + ":" + field;
        }

        private static string Tooltip(StatDef stat, StatCompressionStatConfig config)
        {
            return StatCompressionText.T("StatCompression_Tooltip_Baseline", config.baseline) +
                   "\n" + StatCompressionText.T("StatCompression_Tooltip_Threshold", (config.thresholdFactor * 100f).ToString("0.###")) +
                   "\n" + StatCompressionText.T("StatCompression_Tooltip_Method", StatCompressionText.MethodLabel(config.method)) +
                   "\n" + StatCompressionText.T("StatCompression_Tooltip_TScale", config.tScale) +
                   "\n" + StatCompressionText.T("StatCompression_Tooltip_Direction", StatCompressionText.DirectionShortLabel(config.direction)) +
                   (stat == null ? string.Empty : "\n" + StatCompressionText.T("StatCompression_Tooltip_Category", stat.category?.defName));
        }
    }
}

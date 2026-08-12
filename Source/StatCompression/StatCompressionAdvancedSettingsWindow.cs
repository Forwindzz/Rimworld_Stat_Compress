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
        private const float PreviewRowHeight = 22f;
        private const float SplitGap = 12f;
        private const int ColumnCount = 8;
        private const int GraphSegmentCount = 64;

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
        private static readonly float[] HigherPreviewPercents = { 50f, 100f, 150f, 200f, 500f, 5000f, 100000f };
        private static readonly float[] LowerPreviewPercents = { 150f, 100f, 75f, 40f, 10f, 1f, 0.1f };

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

            var configs = settings.AdvancedConfigs().Where(MatchesSearch).ToList();
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
            var label = LabelFor(config);
            var enabled = config.enabled;
            Widgets.Checkbox(Col(rect, 0).position + new Vector2(2f, 3f), ref enabled);
            config.enabled = enabled;

            Text.Font = GameFont.Tiny;
            Widgets.Label(Col(rect, 1), config.defName);
            Widgets.Label(Col(rect, 2), label);
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

            var directionRect = Col(rect, 7);
            if (SpecialCompressionConfigs.IsDamage(config.defName))
            {
                config.direction = StatCompressionDirection.HigherIsBetter;
                var oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(directionRect, StatCompressionText.DirectionShortLabel(config.direction));
                Text.Anchor = oldAnchor;
            }
            else if (Widgets.ButtonText(directionRect, StatCompressionText.DirectionShortLabel(config.direction)))
            {
                config.direction = config.direction == StatCompressionDirection.HigherIsBetter
                    ? StatCompressionDirection.LowerIsBetter
                    : StatCompressionDirection.HigherIsBetter;
            }
            TooltipHandler.TipRegion(
                directionRect,
                SpecialCompressionConfigs.IsDamage(config.defName)
                    ? StatCompressionText.T("StatCompression_SP_Damage_DirectionTooltip")
                    : StatCompressionText.T("StatCompression_DirectionTooltip"));
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
                : settings.GetAdvancedConfig(selectedDefName);
            if (config == null)
            {
                Widgets.Label(inner, StatCompressionText.T("StatCompression_SelectStatForPreview"));
                return;
            }

            var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 30f),
                LabelFor(config));
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
                ? HigherPreviewPercents
                : LowerPreviewPercents;
            var compiled = StatCompressionRuntimeCompiler.CompileConfig(settings, config);
            var percentColumnWidth = Mathf.Clamp(inner.width * 0.44f, 146f, 190f);
            var columnHeaderY = valuesY + 26f;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(inner.x, columnHeaderY, percentColumnWidth, 18f),
                StatCompressionText.T("StatCompression_AdvancedPreviewPercentColumn"));
            Widgets.Label(
                new Rect(inner.x + percentColumnWidth + 8f, columnHeaderY, inner.width - percentColumnWidth - 8f, 18f),
                StatCompressionText.T("StatCompression_AdvancedPreviewActualColumn"));

            var rowY = columnHeaderY + 20f;
            for (var i = 0; i < percents.Length; i++)
            {
                DrawPreviewValueRow(
                    new Rect(inner.x, rowY + i * PreviewRowHeight, inner.width, PreviewRowHeight),
                    stat,
                    config,
                    ref compiled,
                    percents[i],
                    percentColumnWidth,
                    i);
            }

            var graphTitleY = rowY + percents.Length * PreviewRowHeight + 8f;
            Widgets.DrawLineHorizontal(inner.x, graphTitleY, inner.width);
            Widgets.Label(
                new Rect(inner.x, graphTitleY + 4f, inner.width, 20f),
                StatCompressionText.T("StatCompression_AdvancedPreviewGraph"));
            DrawPreviewGraph(
                new Rect(inner.x, graphTitleY + 26f, inner.width, inner.yMax - graphTitleY - 26f),
                config,
                ref compiled,
                percents);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void DrawPreviewValueRow(
            Rect rect,
            StatDef stat,
            StatCompressionStatConfig config,
            ref CompiledStatConfig compiled,
            float inputPercent,
            float percentColumnWidth,
            int index)
        {
            if ((index & 1) != 0)
            {
                Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            }

            var original = config.baseline * (inputPercent / 100f);
            var final = StatCompressionRuntimeCompiler.ApplyStatic(ref compiled, original);
            var mappedPercent = config.baseline == 0f ? 0f : final / config.baseline * 100f;
            var percentText = FormatPreviewPercent(inputPercent) + " -> " + FormatPreviewPercent(mappedPercent);
            var actualText = FormatStatValue(stat, original) + " -> " + FormatStatValue(stat, final);
            var oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.LabelFit(new Rect(rect.x, rect.y, percentColumnWidth, rect.height), percentText);
            Widgets.LabelFit(
                new Rect(rect.x + percentColumnWidth + 8f, rect.y, rect.width - percentColumnWidth - 8f, rect.height),
                actualText);
            Text.WordWrap = oldWordWrap;
            TooltipHandler.TipRegion(rect, percentText + "    " + actualText);
        }

        private static void DrawPreviewGraph(
            Rect rect,
            StatCompressionStatConfig config,
            ref CompiledStatConfig compiled,
            float[] previewPercents)
        {
            if (rect.height < 90f || rect.width < 220f)
            {
                return;
            }

            var minInput = Math.Max(0.000001f, previewPercents.Min());
            var maxInput = Math.Max(minInput * 1.001f, previewPercents.Max());
            var logMin = (float)Math.Log10(minInput);
            var logMax = (float)Math.Log10(maxInput);
            var maxOutput = 0f;
            for (var i = 0; i <= GraphSegmentCount; i++)
            {
                var inputPercent = LogSample(logMin, logMax, i / (float)GraphSegmentCount);
                var outputPercent = PreviewMappedPercent(config, ref compiled, inputPercent);
                if (!float.IsNaN(outputPercent) && !float.IsInfinity(outputPercent))
                {
                    maxOutput = Math.Max(maxOutput, outputPercent);
                }
            }

            var yMax = NiceAxisMaximum(maxOutput);
            var plot = new Rect(rect.x + 46f, rect.y + 20f, rect.width - 56f, rect.height - 46f);
            Widgets.DrawBoxSolid(plot, new Color(0.08f, 0.09f, 0.1f, 0.72f));

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(plot.x, rect.y, plot.width * 0.5f, 18f),
                StatCompressionText.T("StatCompression_AdvancedPreviewGraphYAxis"));
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(plot.x + plot.width * 0.5f, rect.y, plot.width * 0.5f, 18f),
                StatCompressionText.T("StatCompression_AdvancedPreviewGraphXAxis"));

            var gridColor = new Color(0.55f, 0.57f, 0.59f, 0.28f);
            for (var i = 0; i <= 4; i++)
            {
                var fraction = i / 4f;
                var y = Mathf.Lerp(plot.yMax, plot.y, fraction);
                Widgets.DrawLine(new Vector2(plot.x, y), new Vector2(plot.xMax, y), gridColor, 1f);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(
                    new Rect(rect.x, y - 9f, 42f, 18f),
                    FormatAxisPercent(yMax * fraction));
            }

            var firstDecade = (int)Math.Ceiling(logMin);
            var lastDecade = (int)Math.Floor(logMax);
            for (var decade = firstDecade; decade <= lastDecade; decade++)
            {
                var fraction = (decade - logMin) / (logMax - logMin);
                var x = Mathf.Lerp(plot.x, plot.xMax, fraction);
                Widgets.DrawLine(new Vector2(x, plot.y), new Vector2(x, plot.yMax), gridColor, 1f);
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(
                    new Rect(x - 32f, plot.yMax + 1f, 64f, 18f),
                    FormatAxisPercent((float)Math.Pow(10d, decade)));
            }

            var thresholdPercent = config.direction == StatCompressionDirection.HigherIsBetter
                ? config.thresholdFactor * 100f
                : 100f / config.thresholdFactor;
            if (thresholdPercent >= minInput && thresholdPercent <= maxInput)
            {
                var thresholdX = Mathf.Lerp(
                    plot.x,
                    plot.xMax,
                    ((float)Math.Log10(thresholdPercent) - logMin) / (logMax - logMin));
                Widgets.DrawLine(
                    new Vector2(thresholdX, plot.y),
                    new Vector2(thresholdX, plot.yMax),
                    new Color(0.95f, 0.72f, 0.25f, 0.8f),
                    1.5f);
            }

            var curveColor = new Color(0.35f, 0.78f, 0.92f, 1f);
            var hasPrevious = false;
            var previous = Vector2.zero;
            for (var i = 0; i <= GraphSegmentCount; i++)
            {
                var fraction = i / (float)GraphSegmentCount;
                var inputPercent = LogSample(logMin, logMax, fraction);
                var outputPercent = PreviewMappedPercent(config, ref compiled, inputPercent);
                if (float.IsNaN(outputPercent) || float.IsInfinity(outputPercent))
                {
                    hasPrevious = false;
                    continue;
                }

                var point = new Vector2(
                    Mathf.Lerp(plot.x, plot.xMax, fraction),
                    Mathf.Lerp(plot.yMax, plot.y, Mathf.Clamp01(outputPercent / yMax)));
                if (hasPrevious)
                {
                    Widgets.DrawLine(previous, point, curveColor, 2f);
                }

                previous = point;
                hasPrevious = true;
            }

            for (var i = 0; i < previewPercents.Length; i++)
            {
                var inputPercent = previewPercents[i];
                var outputPercent = PreviewMappedPercent(config, ref compiled, inputPercent);
                if (float.IsNaN(outputPercent) || float.IsInfinity(outputPercent))
                {
                    continue;
                }

                var point = new Vector2(
                    Mathf.Lerp(
                        plot.x,
                        plot.xMax,
                        ((float)Math.Log10(inputPercent) - logMin) / (logMax - logMin)),
                    Mathf.Lerp(plot.yMax, plot.y, Mathf.Clamp01(outputPercent / yMax)));
                Widgets.DrawBoxSolid(new Rect(point.x - 2f, point.y - 2f, 4f, 4f), Color.white);
            }

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            Text.WordWrap = oldWordWrap;
        }

        private static float PreviewMappedPercent(
            StatCompressionStatConfig config,
            ref CompiledStatConfig compiled,
            float inputPercent)
        {
            var original = config.baseline * inputPercent / 100f;
            var mapped = StatCompressionRuntimeCompiler.ApplyStatic(ref compiled, original);
            return mapped / config.baseline * 100f;
        }

        private static float LogSample(float logMin, float logMax, float fraction)
        {
            return (float)Math.Pow(10d, Mathf.Lerp(logMin, logMax, fraction));
        }

        private static float NiceAxisMaximum(float value)
        {
            if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
            {
                return 100f;
            }

            var magnitude = (float)Math.Pow(10d, Math.Floor(Math.Log10(value)));
            var scaled = value / magnitude;
            var nice = scaled <= 1f ? 1f : scaled <= 2f ? 2f : scaled <= 5f ? 5f : 10f;
            return nice * magnitude;
        }

        private static string FormatPreviewPercent(float value)
        {
            return value.ToString("0.###") + "%";
        }

        private static string FormatAxisPercent(float value)
        {
            if (value >= 1000000f)
            {
                return value.ToString("0.##E+0") + "%";
            }

            return value.ToString(value < 1f ? "0.###" : "0.##") + "%";
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
            if (SpecialCompressionConfigs.IsSpecial(config.defName))
            {
                return SpecialCompressionConfigs.LabelFor(config.defName);
            }

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

            if (SpecialCompressionConfigs.IsSpecial(config.defName))
            {
                return SpecialCompressionConfigs.LabelFor(config.defName)
                    .ToLowerInvariant()
                    .Contains(needle);
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
                   (SpecialCompressionConfigs.IsSpecial(config.defName)
                       ? "\n" + StatCompressionText.T("StatCompression_Tooltip_SpecialModule") +
                         (config.defName == SpecialCompressionConfigs.BodyPartHealthDefName
                             ? "\n" + StatCompressionText.T("StatCompression_SP_BodyPartHealth_BaselineTooltip")
                             : SpecialCompressionConfigs.IsDamage(config.defName)
                                 ? "\n" + StatCompressionText.T("StatCompression_SP_Damage_BaselineTooltip")
                                 : string.Empty)
                       : stat == null
                           ? string.Empty
                           : "\n" + StatCompressionText.T("StatCompression_Tooltip_Category", stat.category?.defName));
        }
    }
}

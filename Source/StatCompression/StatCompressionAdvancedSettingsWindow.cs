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
        private const int ColumnCount = 9;
        private const int GraphSegmentCount = 64;

        private static readonly float[] DefaultColumnWidths =
        {
            34f, 36f, 160f, 120f, 88f, 72f, 86f, 86f, 110f
        };

        private static readonly float[] MinimumColumnWidths =
        {
            30f, 32f, 102f, 76f, 68f, 58f, 66f, 66f, 84f
        };

        private readonly StatCompressionSettings settings;
        private readonly Dictionary<string, string> tScaleBuffers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> baselineBuffers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> thresholdBuffers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly float[] columnWidths = (float[])DefaultColumnWidths.Clone();
        private readonly float[] resizeStartWidths = new float[ColumnCount];
        private readonly float[] higherPreviewPercents = { 50f, 100f, 150f, 200f, 500f, 5000f, 100000f };
        private readonly float[] lowerPreviewPercents = { 150f, 100f, 75f, 40f, 10f, 1f, 0.1f };
        private readonly float[] lowerDirectPreviewPercents = { -100f, -50f, 0f, 50f, 100f, 200f };
        private readonly string[] higherPreviewBuffers = new string[7];
        private readonly string[] lowerPreviewBuffers = new string[7];
        private readonly string[] lowerDirectPreviewBuffers = new string[6];
        private readonly HashSet<string> presetSelection = new HashSet<string>(StringComparer.Ordinal);

        private Vector2 scrollPosition;
        private string searchText = string.Empty;
        private string selectedDefName;
        private SortColumn sortColumn = SortColumn.DefName;
        private bool sortAscending = true;
        private bool columnWidthsInitialized;
        private int resizingColumn = -1;
        private float resizeStartMouseX;
        private StatCompressionPreset editingPreset;

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

        public StatCompressionAdvancedSettingsWindow(StatCompressionSettings settings, string focusDefName = null)
        {
            this.settings = settings;
            if (!focusDefName.NullOrEmpty())
            {
                searchText = focusDefName;
                selectedDefName = focusDefName;
            }

            doCloseX = true;
            doCloseButton = false;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            optionalTitle = StatCompressionText.T("StatCompression_AdvancedTitle");
        }

        public override Vector2 InitialSize => new Vector2(1280f, 780f);

        public override void DoWindowContents(Rect inRect)
        {
            settings.EnsureStatConfigs();

            var helpRect = new Rect(inRect.x, inRect.y, inRect.width, 96f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(helpRect.x, helpRect.y, helpRect.width, 30f), StatCompressionText.T("StatCompression_AdvancedHelp"));
            Widgets.Label(new Rect(helpRect.x, helpRect.y + 32f, helpRect.width, 30f), StatCompressionText.T("StatCompression_AdvancedFormulaHelp"));
            Widgets.Label(new Rect(helpRect.x, helpRect.y + 64f, helpRect.width, 30f), StatCompressionText.T("StatCompression_DirectionHelp"));
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
            Widgets.Label(new Rect(searchRect.x, searchRect.y, 64f, searchRect.height), StatCompressionText.T("StatCompression_Search"));
            var actionWidth = editingPreset == null ? 150f : 184f;
            searchText = Widgets.TextField(
                new Rect(searchRect.x + 68f, searchRect.y, searchRect.width - 72f - actionWidth, searchRect.height),
                searchText ?? string.Empty);
            var actionRect = new Rect(searchRect.xMax - actionWidth, searchRect.y, actionWidth, searchRect.height);
            if (editingPreset == null)
            {
                if (Widgets.ButtonText(actionRect, StatCompressionText.T("StatCompression_Preset_LoadEdit")))
                {
                    OpenPresetMenu();
                }
            }
            else
            {
                Widgets.Label(
                    new Rect(actionRect.x, actionRect.y, actionRect.width - 34f, actionRect.height),
                    editingPreset.Name);
                if (Widgets.ButtonText(
                        new Rect(actionRect.xMax - 30f, actionRect.y, 30f, actionRect.height),
                        "X"))
                {
                    ExitPresetEditing();
                }
            }

            var configs = CurrentConfigs().Where(MatchesSearch).ToList();
            SortConfigs(configs);

            var headerRect = new Rect(rect.x, searchRect.yMax + 10f, rect.width - 16f, RowHeight);
            EnsureColumnWidths(headerRect.width);
            HandleColumnResize(headerRect);
            DrawHeader(headerRect, configs);

            if (selectedDefName.NullOrEmpty() && configs.Count > 0)
            {
                selectedDefName = configs[0].defName;
            }

            var footerHeight = 38f;
            var outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.yMax - headerRect.yMax - footerHeight);
            var viewRect = new Rect(0f, 0f, headerRect.width, configs.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            for (var i = 0; i < configs.Count; i++)
            {
                DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), configs[i], i);
            }

            Widgets.EndScrollView();
            DrawPresetFooter(new Rect(rect.x, rect.yMax - 34f, rect.width, 34f));
        }

        private void DrawHeader(Rect rect, List<StatCompressionStatConfig> visibleConfigs)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.18f, 1f));
            var labels = new[]
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
                if (i > 0 && sortColumn == column)
                {
                    label += sortAscending ? " ^" : " v";
                }

                Widgets.Label(cell.ContractedBy(2f), label);
                if (Widgets.ButtonInvisible(cell, false))
                {
                    if (i == 0)
                    {
                        ToggleVisibleSelection(visibleConfigs);
                    }
                    else
                    {
                        SetSort(column);
                    }
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

            var enabled = config.enabled;
            Widgets.Checkbox(Col(rect, 1).position + new Vector2(2f, 3f), ref enabled);
            config.enabled = enabled;

            Text.Font = GameFont.Tiny;
            Widgets.Label(Col(rect, 2), config.defName);
            Widgets.Label(Col(rect, 3), label);
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(Col(rect, 4), StatCompressionText.MethodShortLabel(config.method)))
            {
                OpenMethodMenu(config);
            }

            var tScaleBuffer = GetBuffer(tScaleBuffers, config.defName, "ts", config.tScale);
            Widgets.TextFieldNumeric(Col(rect, 5), ref config.tScale, ref tScaleBuffer, 0.0001f, float.MaxValue);
            tScaleBuffers[BufferKey(config.defName, "ts")] = tScaleBuffer;
            TooltipHandler.TipRegion(Col(rect, 5), StatCompressionText.T("StatCompression_TScaleTooltip"));

            var baselineBuffer = GetBuffer(baselineBuffers, config.defName, "b", config.baseline);
            Widgets.TextFieldNumeric(Col(rect, 6), ref config.baseline, ref baselineBuffer, 1e-10f, float.MaxValue);
            baselineBuffers[BufferKey(config.defName, "b")] = baselineBuffer;

            var thresholdPercent = config.thresholdFactor * 100f;
            var thresholdBuffer = GetBuffer(thresholdBuffers, config.defName, "th", thresholdPercent);
            var thresholdMinimum = config.direction == StatCompressionDirection.LowerIsBetter
                ? 0.0001f
                : float.MinValue;
            Widgets.TextFieldNumeric(Col(rect, 7), ref thresholdPercent, ref thresholdBuffer, thresholdMinimum, float.MaxValue);
            config.thresholdFactor = thresholdPercent / 100f;
            thresholdBuffers[BufferKey(config.defName, "th")] = thresholdBuffer;

            var directionRect = Col(rect, 8);
            var fixedSpecialDirection = SpecialCompressionConfigs.IsDamage(config.defName) ||
                                        SpecialCompressionConfigs.IsHediffStage(config.defName);
            if (fixedSpecialDirection)
            {
                config.direction = SpecialCompressionConfigs.IsDamage(config.defName)
                    ? StatCompressionDirection.HigherIsBetter
                    : SpecialCompressionConfigs.DirectionForHediffStage(config.defName);
                var oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(directionRect, StatCompressionText.DirectionShortLabel(config.direction));
                Text.Anchor = oldAnchor;
            }
            else if (Widgets.ButtonText(directionRect, StatCompressionText.DirectionShortLabel(config.direction)))
            {
                OpenDirectionMenu(config);
            }
            TooltipHandler.TipRegion(
                directionRect,
                SpecialCompressionConfigs.IsDamage(config.defName)
                    ? StatCompressionText.T("StatCompression_SP_Damage_DirectionTooltip")
                    : SpecialCompressionConfigs.IsHediffStage(config.defName)
                        ? StatCompressionText.T("StatCompression_SP_HediffStage_DirectionTooltip")
                    : StatCompressionText.T("StatCompression_DirectionTooltip"));
            TooltipHandler.TipRegion(Col(rect, 2), Tooltip(stat, config));
            TooltipHandler.TipRegion(Col(rect, 3), Tooltip(stat, config));
            StatCompressionSettings.NormalizeConfig(config);
        }

        private void DrawSelectedPreview(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(10f);
            var config = selectedDefName.NullOrEmpty()
                ? null
                : CurrentConfigs().FirstOrDefault(candidate => candidate.defName == selectedDefName);
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
            var actualMethod = StatCompressionRuntime.ResolveMethod(config.method, settings.method);
            var compiled = StatCompressionRuntimeCompiler.CompileConfig(settings, config);
            var thresholdValue = compiled.thresholdValue;
            var summaryY = inner.y + 56f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(inner.x, summaryY, inner.width, 42f),
                StatCompressionText.T(
                    "StatCompression_AdvancedPreviewSummary",
                    StatCompressionText.DirectionShortLabel(config.direction),
                    FormatStatValue(stat, config.baseline),
                    FormatStatValue(stat, thresholdValue)));

            var formula = BuildFormula(config, actualMethod, actualParameter);
            Widgets.Label(new Rect(inner.x, summaryY + 44f, inner.width, 142f), formula);
            Text.Font = GameFont.Small;

            var valuesY = summaryY + 190f;
            Widgets.DrawLineHorizontal(inner.x, valuesY, inner.width);
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(inner.x, valuesY + 4f, inner.width, 22f),
                StatCompressionText.T("StatCompression_AdvancedPreviewValues"));

            GetPreviewValues(config.direction, out var percents, out var percentBuffers);
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
                    percents,
                    percentBuffers,
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
            float[] previewPercents,
            string[] previewBuffers,
            float percentColumnWidth,
            int index)
        {
            if ((index & 1) != 0)
            {
                Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            }

            if (previewBuffers[index] == null)
            {
                previewBuffers[index] = previewPercents[index].ToString("0.###");
            }

            var inputRect = new Rect(rect.x, rect.y + 1f, 56f, rect.height - 2f);
            Widgets.TextFieldNumeric(
                inputRect,
                ref previewPercents[index],
                ref previewBuffers[index],
                float.MinValue,
                float.MaxValue);

            var inputPercent = previewPercents[index];
            var original = config.baseline * (inputPercent / 100f);
            var final = StatCompressionRuntimeCompiler.ApplyStatic(ref compiled, original);
            var mappedPercent = config.baseline == 0f ? 0f : final / config.baseline * 100f;
            var percentText = "% -> " + FormatPreviewPercent(mappedPercent);
            var actualText = FormatStatValue(stat, original) + " -> " + FormatStatValue(stat, final);
            var oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.LabelFit(
                new Rect(inputRect.xMax + 3f, rect.y, percentColumnWidth - inputRect.width - 3f, rect.height),
                percentText);
            Widgets.LabelFit(
                new Rect(rect.x + percentColumnWidth + 8f, rect.y, rect.width - percentColumnWidth - 8f, rect.height),
                actualText);
            Text.WordWrap = oldWordWrap;
            TooltipHandler.TipRegion(rect, percentText + "    " + actualText);
        }

        private void GetPreviewValues(
            StatCompressionDirection direction,
            out float[] values,
            out string[] buffers)
        {
            if (direction == StatCompressionDirection.HigherIsBetter)
            {
                values = higherPreviewPercents;
                buffers = higherPreviewBuffers;
                return;
            }

            if (direction == StatCompressionDirection.LowerDirect)
            {
                values = lowerDirectPreviewPercents;
                buffers = lowerDirectPreviewBuffers;
                return;
            }

            values = lowerPreviewPercents;
            buffers = lowerPreviewBuffers;
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

            var minInput = previewPercents.Min();
            var maxInput = previewPercents.Max();
            var signedAxis = minInput <= 0f;
            if (!signedAxis)
            {
                minInput = Math.Max(0.000001f, minInput);
                maxInput = Math.Max(minInput * 1.001f, maxInput);
            }

            var axisMin = TransformInputAxis(minInput, signedAxis);
            var axisMax = TransformInputAxis(maxInput, signedAxis);
            if (Math.Abs(axisMax - axisMin) < 0.000001f)
            {
                axisMin -= 1f;
                axisMax += 1f;
            }

            var minOutput = 0f;
            var maxOutput = 0f;
            for (var i = 0; i <= GraphSegmentCount; i++)
            {
                var inputPercent = InverseInputAxis(
                    Mathf.Lerp(axisMin, axisMax, i / (float)GraphSegmentCount),
                    signedAxis);
                var outputPercent = PreviewMappedPercent(config, ref compiled, inputPercent);
                if (!float.IsNaN(outputPercent) && !float.IsInfinity(outputPercent))
                {
                    minOutput = Math.Min(minOutput, outputPercent);
                    maxOutput = Math.Max(maxOutput, outputPercent);
                }
            }

            if (Math.Abs(maxOutput - minOutput) < 0.000001f)
            {
                minOutput -= 1f;
                maxOutput += 1f;
            }

            var outputPadding = (maxOutput - minOutput) * 0.06f;
            var yMin = minOutput - outputPadding;
            var yMax = maxOutput + outputPadding;
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
                StatCompressionText.T(signedAxis
                    ? "StatCompression_AdvancedPreviewGraphXAxisSigned"
                    : "StatCompression_AdvancedPreviewGraphXAxis"));

            var gridColor = new Color(0.55f, 0.57f, 0.59f, 0.28f);
            for (var i = 0; i <= 4; i++)
            {
                var fraction = i / 4f;
                var y = Mathf.Lerp(plot.yMax, plot.y, fraction);
                Widgets.DrawLine(new Vector2(plot.x, y), new Vector2(plot.xMax, y), gridColor, 1f);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(
                    new Rect(rect.x, y - 9f, 42f, 18f),
                    FormatAxisPercent(Mathf.Lerp(yMin, yMax, fraction)));
            }

            if (signedAxis)
            {
                for (var i = 0; i <= 4; i++)
                {
                    var fraction = i / 4f;
                    var axisValue = Mathf.Lerp(axisMin, axisMax, fraction);
                    DrawInputGridLine(
                        plot,
                        fraction,
                        FormatAxisPercent(InverseInputAxis(axisValue, true)),
                        gridColor);
                }
            }
            else
            {
                var firstDecade = (int)Math.Ceiling(axisMin);
                var lastDecade = (int)Math.Floor(axisMax);
                for (var decade = firstDecade; decade <= lastDecade; decade++)
                {
                    DrawInputGridLine(
                        plot,
                        (decade - axisMin) / (axisMax - axisMin),
                        FormatAxisPercent((float)Math.Pow(10d, decade)),
                        gridColor);
                }
            }

            var thresholdPercent = config.baseline == 0f
                ? float.NaN
                : compiled.thresholdValue / config.baseline * 100f;
            if (thresholdPercent >= minInput && thresholdPercent <= maxInput)
            {
                var thresholdX = Mathf.Lerp(
                    plot.x,
                    plot.xMax,
                    (TransformInputAxis(thresholdPercent, signedAxis) - axisMin) / (axisMax - axisMin));
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
                var inputPercent = InverseInputAxis(Mathf.Lerp(axisMin, axisMax, fraction), signedAxis);
                var outputPercent = PreviewMappedPercent(config, ref compiled, inputPercent);
                if (float.IsNaN(outputPercent) || float.IsInfinity(outputPercent))
                {
                    hasPrevious = false;
                    continue;
                }

                var point = new Vector2(
                    Mathf.Lerp(plot.x, plot.xMax, fraction),
                    Mathf.Lerp(plot.yMax, plot.y, Mathf.InverseLerp(yMin, yMax, outputPercent)));
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
                        (TransformInputAxis(inputPercent, signedAxis) - axisMin) / (axisMax - axisMin)),
                    Mathf.Lerp(plot.yMax, plot.y, Mathf.InverseLerp(yMin, yMax, outputPercent)));
                Widgets.DrawBoxSolid(new Rect(point.x - 2f, point.y - 2f, 4f, 4f), Color.white);
            }

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            Text.WordWrap = oldWordWrap;
        }

        private static void DrawInputGridLine(Rect plot, float fraction, string label, Color color)
        {
            var x = Mathf.Lerp(plot.x, plot.xMax, fraction);
            Widgets.DrawLine(new Vector2(x, plot.y), new Vector2(x, plot.yMax), color, 1f);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(x - 34f, plot.yMax + 1f, 68f, 18f), label);
        }

        private static float TransformInputAxis(float value, bool signed)
        {
            if (!signed)
            {
                return (float)Math.Log10(Math.Max(0.000001f, value));
            }

            return Math.Sign(value) * (float)Math.Log10(1f + Math.Abs(value) / 100f);
        }

        private static float InverseInputAxis(float value, bool signed)
        {
            if (!signed)
            {
                return (float)Math.Pow(10d, value);
            }

            return Math.Sign(value) * 100f * ((float)Math.Pow(10d, Math.Abs(value)) - 1f);
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

        private string BuildFormula(
            StatCompressionStatConfig config,
            CompressionMethod actualMethod,
            float actualParameter)
        {
            var expression = CompressionExpression(actualMethod);
            string key;
            switch (config.direction)
            {
                case StatCompressionDirection.HigherIsBetter:
                    key = "StatCompression_AdvancedPreviewFormulaHigher";
                    break;
                case StatCompressionDirection.LowerDirect:
                    key = "StatCompression_AdvancedPreviewFormulaLowerDirect";
                    break;
                default:
                    key = "StatCompression_AdvancedPreviewFormulaLower";
                    break;
            }
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
                        config.method = selectedMethod;
                        StatCompressionSettings.NormalizeConfig(config);
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private IEnumerable<StatCompressionStatConfig> CurrentConfigs()
        {
            return editingPreset == null
                ? settings.AdvancedConfigs()
                : editingPreset.Configs;
        }

        private void DrawPresetFooter(Rect rect)
        {
            if (editingPreset != null)
            {
                if (Widgets.ButtonText(rect, StatCompressionText.T("StatCompression_Preset_Save")))
                {
                    if (StatCompressionPresetManager.TrySave(editingPreset, out var error))
                    {
                        Messages.Message(
                            StatCompressionText.T("StatCompression_Preset_Saved", editingPreset.Name),
                            MessageTypeDefOf.TaskCompletion,
                            false);
                        var refreshed = StatCompressionPresetManager.Find(editingPreset.FileName);
                        if (refreshed != null)
                        {
                            editingPreset = StatCompressionPresetManager.Clone(refreshed);
                        }
                    }
                    else
                    {
                        Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                    }
                }

                return;
            }

            if (Widgets.ButtonText(rect, StatCompressionText.T("StatCompression_Preset_CreateFromSelected")))
            {
                var selectedConfigs = settings.AdvancedConfigs()
                    .Where(config => presetSelection.Contains(config.defName))
                    .ToList();
                if (selectedConfigs.Count == 0)
                {
                    Messages.Message(
                        StatCompressionText.T("StatCompression_Preset_ErrorNoSelection"),
                        MessageTypeDefOf.RejectInput,
                        false);
                    return;
                }

                Find.WindowStack.Add(new StatCompressionPresetNameWindow(name =>
                {
                    if (StatCompressionPresetManager.TryCreate(
                            name,
                            selectedConfigs,
                            out var preset,
                            out var error))
                    {
                        Messages.Message(
                            StatCompressionText.T("StatCompression_Preset_Created", preset.Name),
                            MessageTypeDefOf.TaskCompletion,
                            false);
                        presetSelection.Clear();
                    }
                    else
                    {
                        Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                    }
                }));
            }
        }

        private void OpenPresetMenu()
        {
            StatCompressionPresetManager.Refresh();
            var options = StatCompressionPresetManager.Presets
                .Select(preset => new FloatMenuOption(
                    preset.Name,
                    () => EnterPresetEditing(preset)))
                .ToList();
            if (options.Count == 0)
            {
                Messages.Message(
                    StatCompressionText.T("StatCompression_Preset_None"),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void EnterPresetEditing(StatCompressionPreset preset)
        {
            editingPreset = StatCompressionPresetManager.Clone(preset);
            searchText = string.Empty;
            selectedDefName = editingPreset.Configs.FirstOrDefault()?.defName;
            scrollPosition = Vector2.zero;
            ClearFieldBuffers();
        }

        private void ExitPresetEditing()
        {
            editingPreset = null;
            searchText = string.Empty;
            selectedDefName = null;
            scrollPosition = Vector2.zero;
            ClearFieldBuffers();
        }

        private void ToggleVisibleSelection(List<StatCompressionStatConfig> visibleConfigs)
        {
            var select = visibleConfigs.Any(config => !presetSelection.Contains(config.defName));
            for (var i = 0; i < visibleConfigs.Count; i++)
            {
                if (select)
                {
                    presetSelection.Add(visibleConfigs[i].defName);
                }
                else
                {
                    presetSelection.Remove(visibleConfigs[i].defName);
                }
            }
        }

        private void ClearFieldBuffers()
        {
            tScaleBuffers.Clear();
            baselineBuffers.Clear();
            thresholdBuffers.Clear();
        }

        private void OpenDirectionMenu(StatCompressionStatConfig config)
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
                    () => config.direction = selectedDirection));
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

        private string Tooltip(StatDef stat, StatCompressionStatConfig config)
        {
            var actualThreshold = StatCompressionRuntime.GetActualThresholdFactor(
                config.method,
                settings.thresholdFactor,
                config.thresholdFactor);
            return StatCompressionText.T("StatCompression_Tooltip_Baseline", config.baseline) +
                   "\n" + StatCompressionText.T("StatCompression_Tooltip_Threshold", (actualThreshold * 100f).ToString("0.###")) +
                   "\n" + StatCompressionText.T("StatCompression_Tooltip_Method", StatCompressionText.MethodLabel(config.method)) +
                   "\n" + StatCompressionText.T("StatCompression_Tooltip_TScale", config.tScale) +
                   "\n" + StatCompressionText.T("StatCompression_Tooltip_Direction", StatCompressionText.DirectionShortLabel(config.direction)) +
                   (SpecialCompressionConfigs.IsSpecial(config.defName)
                       ? "\n" + StatCompressionText.T("StatCompression_Tooltip_SpecialModule") +
                         (config.defName == SpecialCompressionConfigs.BodyPartHealthDefName
                              ? "\n" + StatCompressionText.T("StatCompression_SP_BodyPartHealth_BaselineTooltip")
                              : SpecialCompressionConfigs.IsDamage(config.defName)
                                  ? "\n" + StatCompressionText.T("StatCompression_SP_Damage_BaselineTooltip")
                                  : SpecialCompressionConfigs.IsHediffStage(config.defName)
                                      ? "\n" + (config.defName == SpecialCompressionConfigs.RegenerationRateDefName
                                          ? StatCompressionText.T("StatCompression_SP_RegenerationRate_BaselineTooltip")
                                          : StatCompressionText.T("StatCompression_SP_HediffStageFactor_BaselineTooltip"))
                                  : string.Empty)
                       : stat == null
                           ? string.Empty
                           : "\n" + StatCompressionText.T("StatCompression_Tooltip_Category", stat.category?.defName));
        }
    }
}

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

        private readonly StatCompressionSettings settings;
        private readonly Dictionary<string, string> tScaleBuffers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> baselineBuffers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> thresholdBuffers = new Dictionary<string, string>(StringComparer.Ordinal);

        private Vector2 scrollPosition;
        private string searchText = string.Empty;

        public StatCompressionAdvancedSettingsWindow(StatCompressionSettings settings)
        {
            this.settings = settings;
            doCloseX = true;
            doCloseButton = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            optionalTitle = StatCompressionText.T("StatCompression_AdvancedTitle");
        }

        public override Vector2 InitialSize => new Vector2(1080f, 760f);

        public override void DoWindowContents(Rect inRect)
        {
            settings.EnsureStatConfigs();

            var helpRect = new Rect(inRect.x, inRect.y, inRect.width, 78f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(helpRect.x, helpRect.y, helpRect.width, 26f), StatCompressionText.T("StatCompression_AdvancedHelp"));
            Widgets.Label(new Rect(helpRect.x, helpRect.y + 24f, helpRect.width, 24f), StatCompressionText.T("StatCompression_AdvancedFormulaHelp"));
            Widgets.Label(new Rect(helpRect.x, helpRect.y + 48f, helpRect.width, 24f), StatCompressionText.T("StatCompression_DirectionHelp"));
            Text.Font = GameFont.Small;

            var searchRect = new Rect(inRect.x, helpRect.yMax + 8f, inRect.width, 30f);
            Widgets.Label(new Rect(searchRect.x, searchRect.y, 90f, searchRect.height), StatCompressionText.T("StatCompression_Search"));
            searchText = Widgets.TextField(new Rect(searchRect.x + 96f, searchRect.y, searchRect.width - 96f, searchRect.height), searchText ?? string.Empty);

            var headerRect = new Rect(inRect.x, searchRect.yMax + 10f, inRect.width - 16f, RowHeight);
            DrawHeader(headerRect);

            var configs = settings.StatConfigs
                .Where(MatchesSearch)
                .ToList();

            var outRect = new Rect(inRect.x, headerRect.yMax, inRect.width, inRect.height - headerRect.yMax - 4f);
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, configs.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            for (var i = 0; i < configs.Count; i++)
            {
                var rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                DrawRow(rowRect, configs[i]);
            }

            Widgets.EndScrollView();
        }

        public override void PostClose()
        {
            settings.NormalizeParameters();
            settings.RebuildLookup();
            StatCompressionRuntime.ClearRuntimeCaches();
            base.PostClose();
        }

        private void DrawHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.18f, 1f));
            Text.Font = GameFont.Tiny;
            Widgets.Label(Col(rect, 0), StatCompressionText.T("StatCompression_Column_On"));
            Widgets.Label(Col(rect, 1), StatCompressionText.T("StatCompression_Column_DefName"));
            Widgets.Label(Col(rect, 2), StatCompressionText.T("StatCompression_Column_Method"));
            Widgets.Label(Col(rect, 3), StatCompressionText.T("StatCompression_Column_TScale"));
            Widgets.Label(Col(rect, 4), StatCompressionText.T("StatCompression_Column_Baseline"));
            Widgets.Label(Col(rect, 5), StatCompressionText.T("StatCompression_Column_Threshold"));
            Widgets.Label(Col(rect, 6), StatCompressionText.T("StatCompression_Column_Direction"));
            Widgets.Label(Col(rect, 7), StatCompressionText.T("StatCompression_Column_Label"));
            Text.Font = GameFont.Small;
        }

        private void DrawRow(Rect rect, StatCompressionStatConfig config)
        {
            if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
            var enabled = config.enabled;
            Widgets.Checkbox(Col(rect, 0).position + new Vector2(2f, 3f), ref enabled);
            config.enabled = enabled;

            Text.Font = GameFont.Tiny;
            Widgets.Label(Col(rect, 1), config.defName);
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(Col(rect, 2), MethodLabel(config.method)))
            {
                CycleMethod(config);
            }

            var tScaleBuffer = GetBuffer(tScaleBuffers, config.defName, "ts", config.tScale);
            Widgets.TextFieldNumeric(Col(rect, 3), ref config.tScale, ref tScaleBuffer, 0.0001f, 1000000f);
            tScaleBuffers[BufferKey(config.defName, "ts")] = tScaleBuffer;
            TooltipHandler.TipRegion(Col(rect, 3), StatCompressionText.T("StatCompression_TScaleTooltip"));

            var baselineBuffer = GetBuffer(baselineBuffers, config.defName, "b", config.baseline);
            Widgets.TextFieldNumeric(Col(rect, 4), ref config.baseline, ref baselineBuffer, 0.000001f, 1000000000f);
            baselineBuffers[BufferKey(config.defName, "b")] = baselineBuffer;

            var thresholdPercent = config.thresholdFactor * 100f;
            var thresholdBuffer = GetBuffer(thresholdBuffers, config.defName, "th", thresholdPercent);
            Widgets.TextFieldNumeric(Col(rect, 5), ref thresholdPercent, ref thresholdBuffer, 1f, 100000f);
            config.thresholdFactor = Math.Max(0.0001f, thresholdPercent / 100f);
            thresholdBuffers[BufferKey(config.defName, "th")] = thresholdBuffer;

            if (Widgets.ButtonText(Col(rect, 6), DirectionLabel(config.direction)))
            {
                config.direction = config.direction == StatCompressionDirection.HigherIsBetter
                    ? StatCompressionDirection.LowerIsBetter
                    : StatCompressionDirection.HigherIsBetter;
            }
            TooltipHandler.TipRegion(Col(rect, 6), StatCompressionText.T("StatCompression_DirectionTooltip"));

            Text.Font = GameFont.Tiny;
            Widgets.Label(Col(rect, 7), stat == null ? string.Empty : stat.LabelCap.ToString());
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(rect, Tooltip(stat, config));
            StatCompressionSettings.NormalizeConfig(config);
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

        private static Rect Col(Rect row, int index)
        {
            var x = row.x;
            var widths = new[] { 34f, 210f, 104f, 78f, 92f, 92f, 126f, row.width - 736f };
            for (var i = 0; i < index; i++)
            {
                x += widths[i];
            }

            return new Rect(x + 2f, row.y + 3f, widths[index] - 4f, row.height - 6f);
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

        private static void CycleMethod(StatCompressionStatConfig config)
        {
            switch (config.method)
            {
                case CompressionMethod.Linear:
                    config.method = CompressionMethod.Exponential;
                    break;
                case CompressionMethod.Exponential:
                    config.method = CompressionMethod.Logarithmic;
                    break;
                case CompressionMethod.Logarithmic:
                    config.method = CompressionMethod.SoftCap;
                    break;
                default:
                    config.method = CompressionMethod.Linear;
                    break;
            }
        }

        private static string MethodLabel(CompressionMethod method)
        {
            return StatCompressionText.MethodShortLabel(method);
        }

        private static string DirectionLabel(StatCompressionDirection direction)
        {
            return StatCompressionText.DirectionShortLabel(direction);
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

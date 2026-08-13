using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed class StatCompressionAdvancedSettingsWindow : Window
    {
        private const float SplitGap = 12f;

        private readonly StatCompressionSettings settings;
        private readonly List<StatCompressionStatConfig> settingsConfigs;
        private readonly AdvancedTableComponent table;
        private readonly AdvancedPreviewComponent preview;
        private readonly AdvancedPresetComponent preset;

        public StatCompressionAdvancedSettingsWindow(
            StatCompressionSettings settings,
            string focusDefName = null)
        {
            this.settings = settings;
            settings.EnsureStatConfigs();
            settingsConfigs = settings.AdvancedConfigs().ToList();
            table = new AdvancedTableComponent(settings, focusDefName);
            preview = new AdvancedPreviewComponent(settings);
            preset = new AdvancedPresetComponent();

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
            var helpRect = new Rect(inRect.x, inRect.y, inRect.width, 96f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(helpRect.x, helpRect.y, helpRect.width, 30f),
                StatCompressionText.T("StatCompression_AdvancedHelp"));
            Widgets.Label(
                new Rect(helpRect.x, helpRect.y + 32f, helpRect.width, 30f),
                StatCompressionText.T("StatCompression_AdvancedFormulaHelp"));
            Widgets.Label(
                new Rect(helpRect.x, helpRect.y + 64f, helpRect.width, 30f),
                StatCompressionText.T("StatCompression_DirectionHelp"));
            Text.Font = GameFont.Small;

            var contentTop = helpRect.yMax + 8f;
            var previewWidth = Mathf.Clamp(inRect.width * 0.34f, 360f, 440f);
            var leftRect = new Rect(
                inRect.x,
                contentTop,
                inRect.width - previewWidth - SplitGap,
                inRect.yMax - contentTop);
            var previewRect = new Rect(
                leftRect.xMax + SplitGap,
                contentTop,
                previewWidth,
                inRect.yMax - contentTop);

            DrawLeftPanel(leftRect);
            preview.SetData(
                table.SelectedConfig,
                new GlobalCompressionInput(settings));
            preview.Draw(previewRect);
        }

        public override void PostClose()
        {
            settings.NormalizeParameters();
            settings.RebuildLookup();
            base.PostClose();
        }

        private void DrawLeftPanel(Rect rect)
        {
            var searchRect = new Rect(rect.x, rect.y, rect.width, 30f);
            var actionWidth = preset.ToolbarWidth;
            table.DrawSearch(new Rect(
                searchRect.x,
                searchRect.y,
                searchRect.width - actionWidth - 4f,
                searchRect.height));
            preset.DrawToolbar(new Rect(
                searchRect.xMax - actionWidth,
                searchRect.y,
                actionWidth,
                searchRect.height));

            table.SetData(preset.GetDataSet(settingsConfigs));

            const float footerHeight = 38f;
            var tableRect = new Rect(
                rect.x,
                searchRect.yMax + 10f,
                rect.width,
                rect.yMax - searchRect.yMax - 10f - footerHeight);
            table.DrawTable(tableRect);

            preset.DrawFooter(
                new Rect(rect.x, rect.yMax - 34f, rect.width, 34f),
                table.GetPresetSelection,
                table.ClearPresetSelection);
        }
    }
}

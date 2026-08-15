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
        private bool showActualParameter;

        public StatCompressionAdvancedSettingsWindow(
            StatCompressionSettings settings,
            string focusDefName = null)
        {
            this.settings = settings;
            settings.EnsureStatConfigs();
            settingsConfigs = settings.AdvancedConfigs().ToList();
            table = new AdvancedTableComponent(settings, focusDefName);
            preview = new AdvancedPreviewComponent(settings);
            preset = new AdvancedPresetComponent(settings);

            doCloseX = true;
            doCloseButton = false;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            optionalTitle = StatCompressionText.T("StatCompression_AdvancedTitle");
        }

        public override Vector2 InitialSize => new Vector2(UI.screenWidth, UI.screenHeight);

        protected override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight).Rounded();
        }

        public override void DoWindowContents(Rect inRect)
        {
            var helpRect = new Rect(inRect.x, inRect.y, inRect.width, 112f);
            var toggleWidth = Mathf.Min(230f, inRect.width * 0.24f);
            var toggleRect = new Rect(
                helpRect.xMax - toggleWidth,
                helpRect.y,
                toggleWidth,
                30f);
            DrawParameterDisplayToggle(toggleRect);

            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(
                    helpRect.x,
                    helpRect.y,
                    helpRect.width - toggleWidth - 12f,
                    helpRect.height),
                StatCompressionText.T("StatCompression_AdvancedSimpleHelp") + "\n" +
                StatCompressionText.T("StatCompression_ObjectFilter_SpecialScope"));
            Text.Font = GameFont.Small;

            var contentTop = helpRect.yMax + 6f;
            var previewWidth = Mathf.Clamp(inRect.width * 0.30f, 420f, 560f);
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

            var global = new GlobalCompressionInput(settings);
            table.ShowActualParameter = showActualParameter;
            DrawLeftPanel(leftRect, global);
            preview.SetData(
                table.SelectedConfig,
                global);
            preview.Draw(previewRect);
        }

        public override void PostClose()
        {
            StatCompressionSettingsEditor.CommitPending(settings);
            base.PostClose();
        }

        private void DrawLeftPanel(Rect rect, GlobalCompressionInput global)
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
            table.SetGlobalInput(global);

            const float footerHeight = 38f;
            var tableRect = new Rect(
                rect.x,
                searchRect.yMax + 10f,
                rect.width,
                rect.yMax - searchRect.yMax - 10f - footerHeight);
            var interaction = table.DrawTable(tableRect);
            if (interaction.ConfigChanged && !preset.IsEditing)
            {
                StatCompressionSettingsEditor.MarkRuntimeChanged();
            }

            preset.DrawFooter(
                new Rect(rect.x, rect.yMax - 34f, rect.width, 34f),
                table.GetPresetSelection,
                table.ClearPresetSelection);
        }

        private void DrawParameterDisplayToggle(Rect rect)
        {
            var half = rect.width / 2f;
            var scaleRect = new Rect(rect.x, rect.y, half - 2f, rect.height);
            var actualRect = new Rect(rect.x + half + 2f, rect.y, half - 2f, rect.height);
            if (!showActualParameter)
            {
                Widgets.DrawBoxSolid(scaleRect, new Color(0.32f, 0.38f, 0.42f, 1f));
            }
            else
            {
                Widgets.DrawBoxSolid(actualRect, new Color(0.32f, 0.38f, 0.42f, 1f));
            }

            if (Widgets.ButtonText(
                    scaleRect,
                    StatCompressionText.T("StatCompression_Display_TScale")))
            {
                showActualParameter = false;
            }
            if (Widgets.ButtonText(
                    actualRect,
                    StatCompressionText.T("StatCompression_Display_ActualT")))
            {
                showActualParameter = true;
            }

            TooltipHandler.TipRegion(
                rect,
                StatCompressionText.T("StatCompression_DisplayT_Tooltip"));
        }
    }
}

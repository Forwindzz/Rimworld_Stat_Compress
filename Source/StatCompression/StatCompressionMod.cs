using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    public sealed class StatCompressionMod : Mod
    {
        private static string parameterBuffer;
        private static string thresholdPercentBuffer;
        private static string lastExportPath;
        private static string lastImportPath;

        public static StatCompressionSettings Settings { get; private set; }
        public static ModContentPack ContentPack { get; private set; }

        public StatCompressionMod(ModContentPack content) : base(content)
        {
            ContentPack = content;
            Settings = GetSettings<StatCompressionSettings>();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Settings.EnsureStatConfigs();
                StatCompressionBootstrap.PatchAll();
            });
        }

        public override string SettingsCategory()
        {
            return StatCompressionText.T("StatCompression_SettingsTitle");
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.EnsureStatConfigs();
            parameterBuffer = parameterBuffer ?? Settings.parameter.ToString();
            thresholdPercentBuffer = thresholdPercentBuffer ?? (Settings.thresholdFactor * 100f).ToString();

            var oldEnabled = Settings.enabled;

            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled(StatCompressionText.T("StatCompression_Enable"), ref Settings.enabled);
            listing.CheckboxLabeled(
                StatCompressionText.T("StatCompression_ShowInfoCardSettingsButton"),
                ref Settings.showInfoCardSettingsButton);
            var methodChangedBySimpleUi = DrawMethodRow(listing);
            var parameterChangedBySimpleUi = DrawParameterRow(listing);
            var thresholdChangedBySimpleUi = DrawThresholdRow(listing);
            DrawPreviewSheet(listing);
            var settingsReplaced = DrawActionButtons(listing);
            settingsReplaced |= DrawResetRow(listing);

            if (!lastExportPath.NullOrEmpty())
            {
                listing.Label(StatCompressionText.T("StatCompression_LastExport", lastExportPath));
            }

            if (!lastImportPath.NullOrEmpty())
            {
                listing.Label(StatCompressionText.T("StatCompression_LastImport", lastImportPath));
            }

            listing.End();

            Settings.NormalizeParameters();
            if (settingsReplaced)
            {
                return;
            }

            var compressionShapeChanged =
                methodChangedBySimpleUi ||
                parameterChangedBySimpleUi ||
                thresholdChangedBySimpleUi;
            if (compressionShapeChanged)
            {
                Settings.ApplyGlobalCompressionToEnabled(methodChangedBySimpleUi);
            }

            if (compressionShapeChanged || oldEnabled != Settings.enabled)
            {
                Settings.RebuildLookup();
            }
        }

        public override void WriteSettings()
        {
            Settings.NormalizeParameters();
            Settings.RebuildLookup();
            base.WriteSettings();
        }

        private static bool DrawMethodRow(Listing_Standard listing)
        {
            var changed = false;
            var rect = listing.GetRect(98f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), StatCompressionText.T("StatCompression_CompressionType"));
            TooltipHandler.TipRegion(rect, StatCompressionText.T("StatCompression_MethodTooltip"));

            var buttonRect = new Rect(rect.x, rect.y + 28f, rect.width, 34f);
            var methods = new[]
            {
                CompressionMethod.Linear,
                CompressionMethod.Exponential,
                CompressionMethod.Logarithmic,
                CompressionMethod.SoftCap
            };
            var buttonWidth = buttonRect.width / methods.Length;
            for (var i = 0; i < methods.Length; i++)
            {
                var method = methods[i];
                var cell = new Rect(buttonRect.x + i * buttonWidth + 2f, buttonRect.y, buttonWidth - 4f, buttonRect.height);
                if (Settings.method == method)
                {
                    Widgets.DrawBoxSolid(cell, new Color(0.32f, 0.38f, 0.42f, 1f));
                }

                if (Widgets.ButtonText(cell, StatCompressionText.MethodLabel(method)) && Settings.method != method)
                {
                    Settings.method = method;
                    Settings.parameter = StatCompressionRuntime.DefaultParameter(Settings.method);
                    parameterBuffer = Settings.parameter.ToString();
                    changed = true;
                }
            }

            Widgets.Label(new Rect(rect.x, rect.y + 68f, rect.width, 24f), FormulaText(Settings.method));
            return changed;
        }

        private static bool DrawParameterRow(Listing_Standard listing)
        {
            var changed = false;
            var range = ParameterRange(Settings.method);
            var rect = listing.GetRect(34f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width * 0.28f, 24f), StatCompressionText.T("StatCompression_ParameterT"));
            TooltipHandler.TipRegion(rect, StatCompressionText.T("StatCompression_ParameterTooltip"));

            var sliderRect = new Rect(rect.x + rect.width * 0.28f, rect.y, rect.width * 0.52f - 8f, 24f);
            var sliderValue = Math.Max(range.min, Math.Min(range.max, Settings.parameter));
            var newValue = Widgets.HorizontalSlider(sliderRect, sliderValue, range.min, range.max, false, null, null, null, SliderRoundTo(Settings.method));
            if (Math.Abs(newValue - sliderValue) > 0.000001f)
            {
                Settings.parameter = newValue;
                parameterBuffer = Settings.parameter.ToString("0.###");
                changed = true;
            }

            var parameterBeforeTextField = Settings.parameter;
            Widgets.TextFieldNumeric(
                new Rect(rect.x + rect.width * 0.8f, rect.y, rect.width * 0.2f, 24f),
                ref Settings.parameter,
                ref parameterBuffer,
                ParameterSafetyMinimum(Settings.method),
                float.MaxValue);
            Settings.parameter = StatCompressionSettings.NormalizeParameter(Settings.method, Settings.parameter);
            return changed || Math.Abs(parameterBeforeTextField - Settings.parameter) > 0.000001f;
        }

        private static bool DrawThresholdRow(Listing_Standard listing)
        {
            var originalThreshold = Settings.thresholdFactor;
            var rect = listing.GetRect(34f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width * 0.28f, 24f), StatCompressionText.T("StatCompression_CompressAbove"));
            TooltipHandler.TipRegion(rect, StatCompressionText.T("StatCompression_CompressAboveTooltip"));

            var percent = Settings.thresholdFactor * 100f;
            var sliderPercent = Widgets.HorizontalSlider(
                new Rect(rect.x + rect.width * 0.28f, rect.y, rect.width * 0.52f - 8f, 24f),
                percent,
                25f,
                1000f,
                false,
                null,
                null,
                null,
                1f);
            if (Math.Abs(sliderPercent - percent) > 0.000001f)
            {
                percent = sliderPercent;
                thresholdPercentBuffer = percent.ToString("0.###");
            }

            Widgets.TextFieldNumeric(new Rect(rect.x + rect.width * 0.8f, rect.y, rect.width * 0.2f - 24f, 24f), ref percent, ref thresholdPercentBuffer, 1f, 100000f);
            Widgets.Label(new Rect(rect.xMax - 20f, rect.y, 20f, 24f), "%");
            Settings.thresholdFactor = Math.Max(0.0001f, percent / 100f);
            return Math.Abs(originalThreshold - Settings.thresholdFactor) > 0.000001f;
        }

        private static void DrawPreviewSheet(Listing_Standard listing)
        {
            var rect = listing.GetRect(185f);
            Widgets.DrawMenuSection(rect);
            rect = rect.ContractedBy(8f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), StatCompressionText.T("StatCompression_Preview"));

            var left = new Rect(rect.x, rect.y + 30f, rect.width, 64f);
            DrawPreviewRow(
                left,
                StatCompressionText.T("StatCompression_GlobalWorkSpeed"),
                StatCompressionText.T("StatCompression_CompressedGlobalWorkSpeed"),
                StatCompressionDirection.HigherIsBetter,
                new[] { 0.5f, 1f, 1.5f, 2f, 5f, 50f, 1000f });

            var right = new Rect(rect.x, rect.y + 102f, rect.width, 64f);
            DrawPreviewRow(
                right,
                StatCompressionText.T("StatCompression_IncomingDamageFactor"),
                StatCompressionText.T("StatCompression_CompressedIncomingDamageFactor"),
                StatCompressionDirection.LowerIsBetter,
                new[] { 1.5f, 1f, 0.75f, 0.4f, 0.1f, 0.01f, 0.001f });
        }

        private static void DrawPreviewRow(Rect rect, string inputTitle, string outputTitle, StatCompressionDirection direction, float[] values)
        {
            var labelWidth = 230f;
            Widgets.Label(new Rect(rect.x, rect.y, labelWidth, 24f), inputTitle);
            Widgets.Label(new Rect(rect.x, rect.y + 28f, labelWidth, 24f), outputTitle);
            var cellWidth = (rect.width - labelWidth) / values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                var input = values[i];
                var output = Preview(input, direction);
                var cell = new Rect(rect.x + labelWidth + i * cellWidth, rect.y, cellWidth - 4f, rect.height);
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(cell.x, cell.y, cell.width, 24f), FormatPercent(input));
                Widgets.Label(new Rect(cell.x, cell.y + 28f, cell.width, 24f), FormatPercent(output));
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private static bool DrawActionButtons(Listing_Standard listing)
        {
            var settingsReplaced = false;
            var rect = listing.GetRect(32f);
            var buttonWidth = rect.width / 3f;
            var advancedRect = new Rect(rect.x, rect.y, buttonWidth - 4f, rect.height);
            var exportRect = new Rect(rect.x + buttonWidth + 2f, rect.y, buttonWidth - 4f, rect.height);
            var importRect = new Rect(rect.x + buttonWidth * 2f + 4f, rect.y, buttonWidth - 4f, rect.height);
            if (Widgets.ButtonText(advancedRect, StatCompressionText.T("StatCompression_AdvancedSettings")))
            {
                Find.WindowStack.Add(new StatCompressionAdvancedSettingsWindow(Settings));
            }

            if (Widgets.ButtonText(exportRect, StatCompressionText.T("StatCompression_ExportXml")))
            {
                lastExportPath = Settings.ExportSettingsToXml();
                Messages.Message(StatCompressionText.T("StatCompression_ExportedMessage", lastExportPath), MessageTypeDefOf.TaskCompletion, false);
            }

            if (Widgets.ButtonText(importRect, StatCompressionText.T("StatCompression_ImportXml")))
            {
                lastImportPath = Settings.ImportSettingsFromXml(out var updated, out var skipped);
                parameterBuffer = Settings.parameter.ToString();
                thresholdPercentBuffer = (Settings.thresholdFactor * 100f).ToString();
                settingsReplaced = true;
                Messages.Message(StatCompressionText.T("StatCompression_ImportedMessage", updated, skipped, lastImportPath), MessageTypeDefOf.TaskCompletion, false);
            }

            return settingsReplaced;
        }

        private static bool DrawResetRow(Listing_Standard listing)
        {
            if (listing.ButtonText(StatCompressionText.T("StatCompression_ResetSettings")))
            {
                Settings.ResetToDefaults();
                parameterBuffer = Settings.parameter.ToString();
                thresholdPercentBuffer = (Settings.thresholdFactor * 100f).ToString();
                return true;
            }

            return false;
        }

        private static float Preview(float value, StatCompressionDirection direction)
        {
            var config = new StatCompressionStatConfig(
                "Preview",
                true,
                Settings.method,
                Settings.parameter,
                1f,
                1f,
                Settings.thresholdFactor,
                direction);
            return StatCompressionRuntime.ComputePreviewValue(Settings, config, value);
        }

        private static FloatRange ParameterRange(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return new FloatRange(0f, 1f);
                case CompressionMethod.Exponential:
                    return new FloatRange(0.001f, 0.999f);
                case CompressionMethod.Logarithmic:
                    return new FloatRange(1.001f, 10f);
                case CompressionMethod.SoftCap:
                    return new FloatRange(1.001f, 100f);
                default:
                    return new FloatRange(1.001f, 10f);
            }
        }

        private static float SliderRoundTo(CompressionMethod method)
        {
            return method == CompressionMethod.SoftCap ? 0.5f : 0.01f;
        }

        private static float ParameterSafetyMinimum(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return 0f;
                case CompressionMethod.Exponential:
                    return 0.001f;
                case CompressionMethod.Logarithmic:
                    return 1.001f;
                case CompressionMethod.SoftCap:
                    return 0.001f;
                default:
                    return 0.001f;
            }
        }

        private static string FormulaText(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_Formula_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_Formula_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_Formula_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_Formula_SoftCap");
                default:
                    return string.Empty;
            }
        }

        private static string FormatPercent(float value)
        {
            return (value * 100f).ToString("0.###") + "%";
        }
    }
}

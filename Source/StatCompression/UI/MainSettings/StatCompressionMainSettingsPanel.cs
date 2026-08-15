using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal static partial class StatCompressionMainSettingsPanel
    {
        private static string parameterBuffer;
        private static string thresholdPercentBuffer;
        private static string lastExportPath;
        private static string lastImportPath;
        private static bool presetSectionExpanded = true;
        private static bool globalSettingsExpanded;
        private static bool configActionsExpanded;
        private static Vector2 settingsScrollPosition;
        private static Vector2 presetScrollPosition;
        private static readonly SimplePreviewCache PreviewCache = new SimplePreviewCache();
        private static readonly CompressionMethod[] SimpleMethods =
        {
            CompressionMethod.Linear,
            CompressionMethod.Exponential,
            CompressionMethod.Logarithmic,
            CompressionMethod.SoftCap
        };

        private static StatCompressionSettings Settings { get; set; }

        internal static void Configure(StatCompressionSettings settings)
        {
            Settings = settings;
        }

        internal static void Draw(Rect inRect)
        {
            Settings.EnsureStatConfigs();
            parameterBuffer = parameterBuffer ?? Settings.parameter.ToString();
            thresholdPercentBuffer = thresholdPercentBuffer ?? (Settings.thresholdFactor * 100f).ToString();

            var contentHeight = EstimateSettingsContentHeight(inRect.height);
            var viewRect = new Rect(
                0f,
                0f,
                inRect.width - 16f,
                contentHeight);
            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            var compressionEnabled = Settings.enabled;
            listing.CheckboxLabeled(
                StatCompressionText.T("StatCompression_Enable"),
                ref compressionEnabled);
            var enabledChanged = compressionEnabled != Settings.enabled;
            if (enabledChanged)
            {
                StatCompressionSettingsEditor.SetCompressionEnabled(Settings, compressionEnabled);
            }

            var showInfoCardSettingsButton = Settings.showInfoCardSettingsButton;
            listing.CheckboxLabeled(
                StatCompressionText.T("StatCompression_ShowInfoCardSettingsButton"),
                ref showInfoCardSettingsButton);
            Settings.showInfoCardSettingsButton = showInfoCardSettingsButton;
            DrawPresetSection(listing);

            if (DrawFoldoutButton(
                    listing,
                    globalSettingsExpanded,
                    StatCompressionText.T("StatCompression_GlobalSimpleSettings")))
            {
                globalSettingsExpanded = !globalSettingsExpanded;
            }

            var methodChangedBySimpleUi = false;
            if (globalSettingsExpanded)
            {
                methodChangedBySimpleUi = DrawMethodRow(listing);
                DrawParameterRow(listing);
                DrawThresholdRow(listing);
                DrawPreviewSheet(listing);
            }

            DrawObjectTargetFilterButton(listing);
            DrawAdvancedSettingsButton(listing);
            var settingsReplaced = DrawConfigActionsSection(listing);

            listing.End();
            Widgets.EndScrollView();

            if (settingsReplaced)
            {
                return;
            }

            if (methodChangedBySimpleUi ||
                enabledChanged ||
                ShouldCommitContinuousInput())
            {
                StatCompressionSettingsEditor.CommitPending(Settings);
            }
        }

        private static void DrawObjectTargetFilterButton(Listing_Standard listing)
        {
            if (DrawWindowButton(
                    listing,
                    StatCompressionText.T("StatCompression_ObjectFilter_Open")))
            {
                Find.WindowStack.Add(new ObjectTargetFilterWindow(Settings));
            }
        }

        private static void DrawPresetSection(Listing_Standard listing)
        {
            if (DrawFoldoutButton(
                    listing,
                    presetSectionExpanded,
                    StatCompressionText.T("StatCompression_UsePresets")))
            {
                presetSectionExpanded = !presetSectionExpanded;
            }

            if (!presetSectionExpanded)
            {
                return;
            }

            var presets = StatCompressionPresetRepository.Presets;
            var rowCount = Math.Max(1, (presets.Count + 1) / 2);
            var visibleRows = Math.Min(4.5f, rowCount);
            var rect = listing.GetRect(visibleRows * 30f + 8f);
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(4f);
            if (presets.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inner, StatCompressionText.T("StatCompression_Preset_None"));
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                var presetUi = StatCompressionPresetService.GetUiSnapshot(Settings);
                var view = new Rect(0f, 0f, inner.width - 16f, rowCount * 30f);
                Widgets.BeginScrollView(inner, ref presetScrollPosition, view);
                var cellWidth = view.width / 2f;
                for (var i = 0; i < presets.Count; i++)
                {
                    var preset = presets[i];
                    var cell = new Rect(
                        (i % 2) * cellWidth,
                        (i / 2) * 30f,
                        cellWidth - 4f,
                        28f);
                    var active = presetUi.IsActive(preset.FileName);
                    var wasActive = active;
                    StatCompressionPresetConflict conflict = null;
                    var hasConflict = !active &&
                                      presetUi.TryGetConflict(
                                          preset.FileName,
                                          out conflict);
                    var oldEnabled = GUI.enabled;
                    GUI.enabled = oldEnabled && !hasConflict;
                    Widgets.CheckboxLabeled(cell, preset.DisplayName, ref active);
                    GUI.enabled = oldEnabled;
                    if (hasConflict)
                    {
                        TooltipHandler.TipRegion(
                            cell,
                            StatCompressionText.T(
                                "StatCompression_Preset_ConflictTooltip",
                                conflict.PresetName,
                                conflict.DefName,
                                conflict.Fields));
                    }

                    if (active == wasActive)
                    {
                        continue;
                    }

                    if (active)
                    {
                        StatCompressionSettingsEditor.ApplyPreset(Settings, preset);
                    }
                    else
                    {
                        StatCompressionSettingsEditor.DisablePreset(Settings, preset);
                    }

                    presetUi = StatCompressionPresetService.GetUiSnapshot(Settings);
                }

                Widgets.EndScrollView();
            }

            DrawPresetSectionHint(listing);
        }

        private static void DrawPresetSectionHint(Listing_Standard listing)
        {
            var text = StatCompressionText.T("StatCompression_PresetSectionHint");
            var oldFont = Text.Font;
            var oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            listing.Label(text);
            GUI.color = oldColor;
            Text.Font = oldFont;
        }

        private static bool DrawFoldoutButton(
            Listing_Standard listing,
            bool expanded,
            string label)
        {
            var clicked = StatCompressionUi.DrawFoldoutBar(
                listing.GetRect(30f),
                expanded,
                label);
            listing.Gap(listing.verticalSpacing);
            return clicked;
        }

        private static bool DrawWindowButton(Listing_Standard listing, string label)
        {
            var clicked = StatCompressionUi.DrawNavigationBar(
                listing.GetRect(30f),
                label);
            listing.Gap(listing.verticalSpacing);
            return clicked;
        }

        private static float EstimateSettingsContentHeight(float viewportHeight)
        {
            var height = 226f;
            if (presetSectionExpanded)
            {
                var presetRows = Math.Max(
                    1,
                    (StatCompressionPresetRepository.Presets.Count + 1) / 2);
                height += Math.Min(4.5f, presetRows) * 30f + 76f;
            }

            if (globalSettingsExpanded)
            {
                height += 410f;
            }

            if (configActionsExpanded)
            {
                height += 170f;
                if (!lastExportPath.NullOrEmpty())
                {
                    height += 30f;
                }
                if (!lastImportPath.NullOrEmpty())
                {
                    height += 30f;
                }
            }

            return Math.Max(viewportHeight, height);
        }

        private static bool DrawMethodRow(Listing_Standard listing)
        {
            var changed = false;
            var rect = listing.GetRect(98f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), StatCompressionText.T("StatCompression_CompressionType"));
            TooltipHandler.TipRegion(rect, StatCompressionText.T("StatCompression_MethodTooltip"));

            var buttonRect = new Rect(rect.x, rect.y + 28f, rect.width, 34f);
            var buttonWidth = buttonRect.width / SimpleMethods.Length;
            for (var i = 0; i < SimpleMethods.Length; i++)
            {
                var method = SimpleMethods[i];
                var cell = new Rect(buttonRect.x + i * buttonWidth + 2f, buttonRect.y, buttonWidth - 4f, buttonRect.height);
                if (Settings.method == method)
                {
                    Widgets.DrawBoxSolid(cell, new Color(0.32f, 0.38f, 0.42f, 1f));
                }

                if (Widgets.ButtonText(cell, StatCompressionText.MethodLabel(method)) && Settings.method != method)
                {
                    StatCompressionSettingsEditor.SetGlobalMethod(Settings, method);
                    parameterBuffer = Settings.parameter.ToString();
                    changed = true;
                }
            }

            var descriptionRect = new Rect(rect.x, rect.y + 68f, rect.width, 24f);
            Widgets.Label(descriptionRect, MethodDescription(Settings.method));
            TooltipHandler.TipRegion(descriptionRect, FormulaText(Settings.method));
            return changed;
        }

        private static bool DrawParameterRow(Listing_Standard listing)
        {
            var originalParameter = Settings.parameter;
            var range = ParameterRange(Settings.method);
            var rect = listing.GetRect(52f);
            var labelRect = new Rect(rect.x, rect.y, rect.width * 0.32f, 24f);
            Widgets.LabelFit(labelRect, ParameterLabel(Settings.method));
            TooltipHandler.TipRegion(rect, ParameterTooltip(Settings.method));

            var sliderRect = new Rect(rect.x + rect.width * 0.32f, rect.y, rect.width * 0.48f - 8f, 24f);
            var editedParameter = Settings.parameter;
            var sliderValue = Math.Max(range.min, Math.Min(range.max, editedParameter));
            var newValue = Widgets.HorizontalSlider(sliderRect, sliderValue, range.min, range.max, false, null, null, null, SliderRoundTo(Settings.method));
            if (Math.Abs(newValue - sliderValue) > 0.000001f)
            {
                editedParameter = newValue;
                parameterBuffer = editedParameter.ToString("0.###");
            }

            Widgets.TextFieldNumeric(
                new Rect(rect.x + rect.width * 0.8f, rect.y, rect.width * 0.2f, 24f),
                ref editedParameter,
                ref parameterBuffer,
                ParameterSafetyMinimum(Settings.method),
                float.MaxValue);
            StatCompressionSettingsEditor.SetGlobalParameter(Settings, editedParameter);

            var oldFont = Text.Font;
            var oldColor = GUI.color;
            Text.Font = GameFont.Tiny;
            var amplifies = (Settings.method == CompressionMethod.Linear ||
                             Settings.method == CompressionMethod.Exponential) &&
                            Settings.parameter > 1f;
            if (amplifies)
            {
                GUI.color = new Color(1f, 0.63f, 0.42f, 1f);
            }
            Widgets.Label(
                new Rect(rect.x, rect.y + 28f, rect.width, 22f),
                amplifies
                    ? StatCompressionText.T("StatCompression_ParameterAmplifiesWarning")
                    : ParameterDirectionDescription(Settings.method));
            GUI.color = oldColor;
            Text.Font = oldFont;
            return Math.Abs(originalParameter - Settings.parameter) > 0.000001f;
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
            StatCompressionSettingsEditor.SetGlobalThreshold(Settings, percent / 100f);
            return Math.Abs(originalThreshold - Settings.thresholdFactor) > 0.000001f;
        }

        private static void DrawPreviewSheet(Listing_Standard listing)
        {
            PreviewCache.SetInput(Settings);
            var rect = listing.GetRect(207f);
            Widgets.DrawMenuSection(rect);
            rect = rect.ContractedBy(8f);
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), StatCompressionText.T("StatCompression_Preview"));
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(rect.x, rect.y + 24f, rect.width, 22f),
                StatCompressionText.T(
                    "StatCompression_PreviewCurrentMethod",
                    StatCompressionText.MethodLabel(Settings.method),
                    Settings.parameter.ToString("0.###"),
                    ParameterMeaning(Settings.method)));
            Text.Font = GameFont.Small;

            var left = new Rect(rect.x, rect.y + 50f, rect.width, 64f);
            DrawPreviewRow(
                left,
                StatCompressionText.T("StatCompression_GlobalWorkSpeed"),
                StatCompressionText.T("StatCompression_CompressedGlobalWorkSpeed"),
                PreviewCache.HigherInputs,
                PreviewCache.HigherOutputs);

            var right = new Rect(rect.x, rect.y + 122f, rect.width, 64f);
            DrawPreviewRow(
                right,
                StatCompressionText.T("StatCompression_IncomingDamageFactor"),
                StatCompressionText.T("StatCompression_CompressedIncomingDamageFactor"),
                PreviewCache.LowerInputs,
                PreviewCache.LowerOutputs);
        }

        private static void DrawPreviewRow(
            Rect rect,
            string inputTitle,
            string outputTitle,
            string[] inputTexts,
            string[] outputTexts)
        {
            var labelWidth = 230f;
            Widgets.Label(new Rect(rect.x, rect.y, labelWidth, 24f), inputTitle);
            Widgets.Label(new Rect(rect.x, rect.y + 28f, labelWidth, 24f), outputTitle);
            var cellWidth = (rect.width - labelWidth) / inputTexts.Length;
            for (var i = 0; i < inputTexts.Length; i++)
            {
                var cell = new Rect(rect.x + labelWidth + i * cellWidth, rect.y, cellWidth - 4f, rect.height);
                Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(cell.x, cell.y, cell.width, 24f), inputTexts[i]);
                Widgets.Label(new Rect(cell.x, cell.y + 28f, cell.width, 24f), outputTexts[i]);
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private static void DrawAdvancedSettingsButton(Listing_Standard listing)
        {
            if (DrawWindowButton(
                    listing,
                    StatCompressionText.T("StatCompression_AdvancedSettings")))
            {
                Find.WindowStack.Add(new StatCompressionAdvancedSettingsWindow(Settings));
            }
        }

        private static bool ShouldCommitContinuousInput()
        {
            var current = Event.current;
            return current != null &&
                   (current.rawType == EventType.MouseUp ||
                    current.type == EventType.KeyDown &&
                    (current.keyCode == KeyCode.Return ||
                     current.keyCode == KeyCode.KeypadEnter));
        }
    }
}

using System;
using System.IO;
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
        private static bool presetSectionExpanded = true;
        private static bool globalSettingsExpanded;
        private static bool configActionsExpanded;
        private static Vector2 settingsScrollPosition;
        private static Vector2 presetScrollPosition;

        private static readonly Color FoldoutButtonTint =
            new Color(0.68f, 0.82f, 1f, 1f);

        public static StatCompressionSettings Settings { get; private set; }
        public static ModContentPack ContentPack { get; private set; }

        public StatCompressionMod(ModContentPack content) : base(content)
        {
            ContentPack = content;
            var globalSettingsExisted = File.Exists(GlobalSettingsPath());
            Settings = GetSettings<StatCompressionSettings>();
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Settings.EnsureStatConfigs();
                var seedResult = StatCompressionDefaultPresetSeeder.EnsureLocalTemplates();
                StatCompressionPresetManager.Refresh();
                var appliedCount = 0;
                var skippedMissingConfigs = 0;
                if (!globalSettingsExisted &&
                    !seedResult.Failed &&
                    StatCompressionDefaultPresetSeeder.TryApplyDefaults(
                        Settings,
                        out appliedCount,
                        out skippedMissingConfigs))
                {
                    WriteSettings();
                }

                if (seedResult.CreatedCount > 0)
                {
                    if (globalSettingsExisted)
                    {
                        Log.Message(
                            $"[{StatCompressionConstants.DisplayName}] Created {seedResult.CreatedCount} " +
                            "local default presets; existing global configuration was left unchanged.");
                    }
                    else if (appliedCount > 0)
                    {
                        Log.Message(
                            $"[{StatCompressionConstants.DisplayName}] Created {seedResult.CreatedCount} " +
                            $"local default presets; applied {appliedCount} for a new global configuration; " +
                            $"skippedMissingConfigs={skippedMissingConfigs}.");
                    }
                }
                else if (!globalSettingsExisted && appliedCount > 0)
                {
                    Log.Message(
                        $"[{StatCompressionConstants.DisplayName}] Applied {appliedCount} existing " +
                        $"local default presets for a new global configuration; " +
                        $"skippedMissingConfigs={skippedMissingConfigs}.");
                }

                StatCompressionBootstrap.PatchAll();
            });
        }

        private static string GlobalSettingsPath()
        {
            var fileName =
                "Mod_" + typeof(StatCompressionMod).FullName.Replace('.', '_') + ".xml";
            return Path.Combine(GenFilePaths.ConfigFolderPath, fileName);
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

            var contentHeight = EstimateSettingsContentHeight(inRect.height);
            var viewRect = new Rect(
                0f,
                0f,
                inRect.width - 16f,
                contentHeight);
            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect);

            var listing = new Listing_Standard();
            listing.Begin(viewRect);

            listing.CheckboxLabeled(StatCompressionText.T("StatCompression_Enable"), ref Settings.enabled);
            listing.CheckboxLabeled(
                StatCompressionText.T("StatCompression_ShowInfoCardSettingsButton"),
                ref Settings.showInfoCardSettingsButton);
            DrawPresetSection(listing);

            if (DrawFoldoutButton(
                    listing,
                    globalSettingsExpanded,
                    StatCompressionText.T("StatCompression_GlobalSimpleSettings")))
            {
                globalSettingsExpanded = !globalSettingsExpanded;
            }

            var methodChangedBySimpleUi = false;
            var parameterChangedBySimpleUi = false;
            var thresholdChangedBySimpleUi = false;
            if (globalSettingsExpanded)
            {
                methodChangedBySimpleUi = DrawMethodRow(listing);
                parameterChangedBySimpleUi = DrawParameterRow(listing);
                thresholdChangedBySimpleUi = DrawThresholdRow(listing);
                DrawPreviewSheet(listing);
            }

            DrawAdvancedSettingsButton(listing);
            var settingsReplaced = DrawConfigActionsSection(listing);

            listing.End();
            Widgets.EndScrollView();

            Settings.NormalizeParameters();
            if (settingsReplaced)
            {
                return;
            }

            if (thresholdChangedBySimpleUi)
            {
                ApplyGlobalThresholdToEnabledConfigs();
            }

            var compressionShapeChanged =
                methodChangedBySimpleUi ||
                parameterChangedBySimpleUi ||
                thresholdChangedBySimpleUi;
            if (compressionShapeChanged)
            {
                Settings.RebuildLookup();
            }

            if (!compressionShapeChanged && oldEnabled != Settings.enabled)
            {
                Settings.RebuildLookup();
            }
        }

        private static void ApplyGlobalThresholdToEnabledConfigs()
        {
            foreach (var config in Settings.AdvancedConfigs())
            {
                if (config.enabled)
                {
                    config.thresholdFactor = Settings.thresholdFactor;
                }
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

            var presets = StatCompressionPresetManager.Presets;
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
                    var active = Settings.activePresets.Any(name =>
                        string.Equals(name, preset.FileName, StringComparison.OrdinalIgnoreCase));
                    var wasActive = active;
                    StatCompressionPresetConflict conflict = null;
                    var hasConflict = !active &&
                                      StatCompressionPresetManager.TryFindConflict(
                                          Settings,
                                          preset,
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
                        StatCompressionPresetManager.Apply(Settings, preset);
                    }
                    else
                    {
                        StatCompressionPresetManager.Disable(Settings, preset);
                    }
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
            var oldColor = GUI.color;
            GUI.color = FoldoutButtonTint;
            var clicked = listing.ButtonText((expanded ? "- " : "+ ") + label);
            GUI.color = oldColor;
            return clicked;
        }

        private static float EstimateSettingsContentHeight(float viewportHeight)
        {
            var height = 190f;
            if (presetSectionExpanded)
            {
                var presetRows = Math.Max(
                    1,
                    (StatCompressionPresetManager.Presets.Count + 1) / 2);
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

            var descriptionRect = new Rect(rect.x, rect.y + 68f, rect.width, 24f);
            Widgets.Label(descriptionRect, MethodDescription(Settings.method));
            TooltipHandler.TipRegion(descriptionRect, FormulaText(Settings.method));
            return changed;
        }

        private static bool DrawParameterRow(Listing_Standard listing)
        {
            var changed = false;
            var range = ParameterRange(Settings.method);
            var rect = listing.GetRect(52f);
            var labelRect = new Rect(rect.x, rect.y, rect.width * 0.32f, 24f);
            Widgets.LabelFit(labelRect, ParameterLabel(Settings.method));
            TooltipHandler.TipRegion(rect, ParameterTooltip(Settings.method));

            var sliderRect = new Rect(rect.x + rect.width * 0.32f, rect.y, rect.width * 0.48f - 8f, 24f);
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
                StatCompressionDirection.HigherIsBetter,
                new[] { 0.5f, 1f, 1.5f, 2f, 5f, 50f, 1000f });

            var right = new Rect(rect.x, rect.y + 122f, rect.width, 64f);
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

        private static void DrawAdvancedSettingsButton(Listing_Standard listing)
        {
            if (listing.ButtonText(StatCompressionText.T("StatCompression_AdvancedSettings")))
            {
                Find.WindowStack.Add(new StatCompressionAdvancedSettingsWindow(Settings));
            }
        }

        private static bool DrawConfigActionsSection(Listing_Standard listing)
        {
            if (DrawFoldoutButton(
                    listing,
                    configActionsExpanded,
                    StatCompressionText.T("StatCompression_ConfigActions")))
            {
                configActionsExpanded = !configActionsExpanded;
            }

            if (!configActionsExpanded)
            {
                return false;
            }

            var settingsReplaced = false;
            var xmlRect = listing.GetRect(32f);
            var halfWidth = xmlRect.width / 2f;
            var exportRect = new Rect(xmlRect.x, xmlRect.y, halfWidth - 2f, xmlRect.height);
            var importRect = new Rect(xmlRect.x + halfWidth + 2f, xmlRect.y, halfWidth - 2f, xmlRect.height);

            if (Widgets.ButtonText(
                    exportRect,
                    StatCompressionText.T("StatCompression_ExportAllSettingsXml")))
            {
                try
                {
                    lastExportPath = Settings.ExportSettingsToXml();
                    Messages.Message(
                        StatCompressionText.T(
                            "StatCompression_ExportedMessage",
                            lastExportPath),
                        MessageTypeDefOf.TaskCompletion,
                        false);
                }
                catch (Exception ex)
                {
                    Messages.Message(
                        StatCompressionText.T(
                            "StatCompression_ExportSettingsFailed",
                            ex.GetType().Name + ": " + ex.Message),
                        MessageTypeDefOf.RejectInput,
                        false);
                }
            }

            if (Widgets.ButtonText(
                    importRect,
                    StatCompressionText.T("StatCompression_ImportAllSettingsXml")))
            {
                lastImportPath = Settings.ImportSettingsFromXml(
                    out var updated,
                    out var skipped,
                    out var error);
                if (error.NullOrEmpty())
                {
                    parameterBuffer = Settings.parameter.ToString();
                    thresholdPercentBuffer = (Settings.thresholdFactor * 100f).ToString();
                    settingsReplaced = true;
                    Messages.Message(
                        StatCompressionText.T(
                            "StatCompression_ImportedMessage",
                            updated,
                            skipped,
                            lastImportPath),
                        MessageTypeDefOf.TaskCompletion,
                        false);
                }
                else
                {
                    Messages.Message(
                        StatCompressionText.T(
                            "StatCompression_ImportSettingsFailed",
                            error),
                        MessageTypeDefOf.RejectInput,
                        false);
                }
            }

            var presetRect = listing.GetRect(32f);
            var copyRect = new Rect(
                presetRect.x,
                presetRect.y,
                halfWidth - 2f,
                presetRect.height);
            var pasteRect = new Rect(
                presetRect.x + halfWidth + 2f,
                presetRect.y,
                halfWidth - 2f,
                presetRect.height);
            if (Widgets.ButtonText(
                    copyRect,
                    StatCompressionText.T("StatCompression_ExportPresetClipboard")))
            {
                OpenPresetClipboardExportMenu();
            }
            if (Widgets.ButtonText(
                    pasteRect,
                    StatCompressionText.T("StatCompression_ImportPresetClipboard")))
            {
                ImportPresetFromClipboard();
            }

            if (listing.ButtonText(
                    StatCompressionText.T("StatCompression_RestoreDefaultPresets")))
            {
                RequestRestoreDefaultPresetsConfirmation();
            }

            if (listing.ButtonText(StatCompressionText.T("StatCompression_ResetAllSettings")))
            {
                RequestResetConfirmation();
            }

            if (!lastExportPath.NullOrEmpty())
            {
                listing.Label(StatCompressionText.T("StatCompression_LastExport", lastExportPath));
            }
            if (!lastImportPath.NullOrEmpty())
            {
                listing.Label(StatCompressionText.T("StatCompression_LastImport", lastImportPath));
            }

            return settingsReplaced;
        }

        private static void OpenPresetClipboardExportMenu()
        {
            StatCompressionPresetManager.Refresh();
            var presets = StatCompressionPresetManager.Presets;
            if (presets.Count == 0)
            {
                Messages.Message(
                    StatCompressionText.T("StatCompression_Preset_None"),
                    MessageTypeDefOf.NeutralEvent,
                    false);
                return;
            }

            var options = new System.Collections.Generic.List<FloatMenuOption>(presets.Count);
            for (var i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                options.Add(new FloatMenuOption(
                    preset.DisplayName,
                    () =>
                    {
                        GUIUtility.systemCopyBuffer =
                            StatCompressionPresetXml.CreateDocument(preset).ToString();
                        Messages.Message(
                            StatCompressionText.T(
                                "StatCompression_PresetCopied",
                                preset.DisplayName),
                            MessageTypeDefOf.TaskCompletion,
                            false);
                    }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ImportPresetFromClipboard()
        {
            var xml = GUIUtility.systemCopyBuffer;
            if (xml.NullOrEmpty())
            {
                Messages.Message(
                    StatCompressionText.T("StatCompression_PresetClipboardEmpty"),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (!StatCompressionPresetXml.TryParse(xml, out var preset, out var parseError))
            {
                Messages.Message(
                    StatCompressionText.T(
                        "StatCompression_PresetClipboardInvalid",
                        parseError),
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            if (!StatCompressionPresetManager.TryGetImportCollision(
                    preset,
                    out var existing,
                    out var validationError))
            {
                Messages.Message(validationError, MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (existing == null)
            {
                SaveImportedPreset(preset, false);
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                StatCompressionText.T(
                    "StatCompression_PresetOverwriteConfirm",
                    existing.DisplayName),
                () => SaveImportedPreset(preset, true),
                true,
                StatCompressionText.T("StatCompression_PresetOverwriteTitle")));
        }

        private static void SaveImportedPreset(
            StatCompressionPreset preset,
            bool overwrite)
        {
            if (StatCompressionPresetManager.TryImport(
                    preset,
                    overwrite,
                    out var imported,
                    out var error))
            {
                Messages.Message(
                    StatCompressionText.T(
                        "StatCompression_PresetImported",
                        imported.DisplayName),
                    MessageTypeDefOf.TaskCompletion,
                    false);
            }
            else
            {
                Messages.Message(error, MessageTypeDefOf.RejectInput, false);
            }
        }

        private static void RequestResetConfirmation()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                StatCompressionText.T("StatCompression_ResetConfirmText"),
                ResetAllSettings,
                true,
                StatCompressionText.T("StatCompression_ResetConfirmTitle")));
        }

        private static void RequestRestoreDefaultPresetsConfirmation()
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                StatCompressionText.T("StatCompression_RestoreDefaultPresetsConfirmText"),
                RestoreDefaultPresets,
                true,
                StatCompressionText.T("StatCompression_RestoreDefaultPresetsConfirmTitle")));
        }

        private static void RestoreDefaultPresets()
        {
            if (StatCompressionDefaultPresetSeeder.TryReplaceWithDefaults(
                    Settings,
                    out var deletedCount,
                    out var appliedCount,
                    out var skippedMissingConfigs,
                    out var error))
            {
                presetScrollPosition = Vector2.zero;
                Messages.Message(
                    StatCompressionText.T(
                        "StatCompression_RestoreDefaultPresetsCompleted",
                        deletedCount,
                        appliedCount,
                        skippedMissingConfigs),
                    MessageTypeDefOf.TaskCompletion,
                    false);
                return;
            }

            Messages.Message(
                StatCompressionText.T(
                    "StatCompression_RestoreDefaultPresetsFailed",
                    error),
                MessageTypeDefOf.RejectInput,
                false);
        }

        private static void ResetAllSettings()
        {
            Settings.ResetToDefaults();
            parameterBuffer = Settings.parameter.ToString();
            thresholdPercentBuffer = (Settings.thresholdFactor * 100f).ToString();
            Settings.RebuildLookup();
            Messages.Message(
                StatCompressionText.T("StatCompression_ResetCompleted"),
                MessageTypeDefOf.TaskCompletion,
                false);
        }

        private static float Preview(float value, StatCompressionDirection direction)
        {
            var config = new StatCompressionStatConfig(
                "Preview",
                true,
                CompressionMethod.FollowGlobal,
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

        private static string MethodDescription(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_MethodDescription_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_MethodDescription_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_MethodDescription_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_MethodDescription_SoftCap");
                default:
                    return string.Empty;
            }
        }

        private static string ParameterLabel(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_ParameterLabel_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_ParameterLabel_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_ParameterLabel_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_ParameterLabel_SoftCap");
                default:
                    return StatCompressionText.T("StatCompression_ParameterT");
            }
        }

        private static string ParameterDirectionDescription(CompressionMethod method)
        {
            return StatCompressionText.T(
                method == CompressionMethod.Logarithmic
                    ? "StatCompression_ParameterDirection_Larger"
                    : "StatCompression_ParameterDirection_Smaller");
        }

        private static string ParameterMeaning(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_SoftCap");
                default:
                    return string.Empty;
            }
        }

        private static string ParameterTooltip(CompressionMethod method)
        {
            return StatCompressionText.T("StatCompression_ParameterTooltip") +
                   "\n" +
                   ParameterDirectionDescription(method) +
                   "\n" +
                   FormulaText(method);
        }

        private static string FormatPercent(float value)
        {
            return (value * 100f).ToString("0.###") + "%";
        }
    }
}

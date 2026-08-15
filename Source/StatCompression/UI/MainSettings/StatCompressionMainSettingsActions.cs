using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal static partial class StatCompressionMainSettingsPanel
    {
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
                    StatCompressionSettingsEditor.CompleteSettingsImport(Settings);
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
            StatCompressionPresetRepository.Refresh();
            var presets = StatCompressionPresetRepository.Presets;
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

            if (!StatCompressionPresetRepository.TryGetImportCollision(
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
            if (StatCompressionPresetRepository.TryImport(
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
            StatCompressionSettingsEditor.ResetToDefaults(Settings);
            parameterBuffer = Settings.parameter.ToString();
            thresholdPercentBuffer = (Settings.thresholdFactor * 100f).ToString();
            Messages.Message(
                StatCompressionText.T("StatCompression_ResetCompleted"),
                MessageTypeDefOf.TaskCompletion,
                false);
        }

    }
}

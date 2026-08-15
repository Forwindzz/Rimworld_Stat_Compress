using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed class AdvancedPresetComponent
    {
        private readonly StatCompressionSettings settings;
        private StatCompressionPreset editingPreset;
        private int structureVersion;

        public AdvancedPresetComponent(StatCompressionSettings settings)
        {
            this.settings = settings;
        }

        public bool IsEditing => editingPreset != null;
        public float ToolbarWidth => IsEditing ? 184f : 150f;

        public AdvancedDataSet GetDataSet(
            IReadOnlyList<StatCompressionStatConfig> settingsConfigs)
        {
            if (editingPreset != null)
            {
                return new AdvancedDataSet(
                    AdvancedDataSourceKind.Preset,
                    editingPreset,
                    structureVersion,
                    editingPreset.DisplayName,
                    editingPreset.Configs);
            }

            return new AdvancedDataSet(
                AdvancedDataSourceKind.Settings,
                settingsConfigs,
                structureVersion,
                null,
                settingsConfigs);
        }

        public void DrawToolbar(Rect rect)
        {
            if (editingPreset == null)
            {
                if (Widgets.ButtonText(
                        rect,
                        StatCompressionText.T("StatCompression_Preset_LoadEdit")))
                {
                    OpenPresetMenu();
                }

                return;
            }

            Widgets.Label(
                new Rect(rect.x, rect.y, rect.width - 34f, rect.height),
                editingPreset.DisplayName);
            if (Widgets.ButtonText(
                    new Rect(rect.xMax - 30f, rect.y, 30f, rect.height),
                    "X"))
            {
                ExitPresetEditing();
            }
        }

        public void DrawFooter(
            Rect rect,
            Func<IReadOnlyList<StatCompressionStatConfig>> getSelectedConfigs,
            Action clearSelection)
        {
            if (editingPreset != null)
            {
                DrawSaveButton(rect);
                return;
            }

            if (!Widgets.ButtonText(
                    rect,
                    StatCompressionText.T("StatCompression_Preset_CreateFromSelected")))
            {
                return;
            }

            var selectedConfigs = getSelectedConfigs();
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
                StatCompressionPreset preset;
                string error;
                if (StatCompressionPresetRepository.TryCreate(
                        name,
                        selectedConfigs,
                        out preset,
                        out error))
                {
                    Messages.Message(
                        StatCompressionText.T(
                            "StatCompression_Preset_Created",
                            preset.DisplayName),
                        MessageTypeDefOf.TaskCompletion,
                        false);
                    clearSelection();
                }
                else
                {
                    Messages.Message(error, MessageTypeDefOf.RejectInput, false);
                }
            }));
        }

        private void DrawSaveButton(Rect rect)
        {
            if (!Widgets.ButtonText(
                    rect,
                    StatCompressionText.T("StatCompression_Preset_Save")))
            {
                return;
            }

            string error;
            if (StatCompressionPresetRepository.TrySave(editingPreset, out error))
            {
                Messages.Message(
                    StatCompressionText.T(
                        "StatCompression_Preset_Saved",
                        editingPreset.DisplayName),
                    MessageTypeDefOf.TaskCompletion,
                    false);
                var refreshed = StatCompressionPresetRepository.Find(editingPreset.FileName);
                if (refreshed != null)
                {
                    StatCompressionSettingsEditor.ApplyActivePresetUpdate(settings, refreshed);
                    editingPreset = StatCompressionPresetRepository.Clone(refreshed);
                    structureVersion++;
                }
            }
            else
            {
                Messages.Message(error, MessageTypeDefOf.RejectInput, false);
            }
        }

        private void OpenPresetMenu()
        {
            StatCompressionPresetRepository.Refresh();
            var options = StatCompressionPresetRepository.Presets
                .Select(preset => new FloatMenuOption(
                    preset.DisplayName,
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
            editingPreset = StatCompressionPresetRepository.Clone(preset);
            structureVersion++;
        }

        private void ExitPresetEditing()
        {
            editingPreset = null;
            structureVersion++;
        }
    }
}

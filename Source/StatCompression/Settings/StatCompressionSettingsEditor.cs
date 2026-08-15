using System;
using System.Collections.Generic;

namespace StatCompression
{
    internal static class StatCompressionSettingsEditor
    {
        public static void SetCompressionEnabled(
            StatCompressionSettings settings,
            bool enabled)
        {
            if (settings.enabled == enabled)
            {
                return;
            }

            settings.enabled = enabled;
            MarkRuntimeChanged();
        }

        public static void SetGlobalMethod(
            StatCompressionSettings settings,
            CompressionMethod method)
        {
            if (settings.method == method)
            {
                return;
            }

            settings.method = method;
            settings.parameter = StatCompressionRuntime.DefaultParameter(method);
            MarkRuntimeChanged();
        }

        public static void SetGlobalParameter(
            StatCompressionSettings settings,
            float parameter)
        {
            parameter = StatCompressionSettings.NormalizeParameter(settings.method, parameter);
            if (Math.Abs(settings.parameter - parameter) <= 0.000001f)
            {
                return;
            }

            settings.parameter = parameter;
            MarkRuntimeChanged();
        }

        public static void SetGlobalThreshold(
            StatCompressionSettings settings,
            float thresholdFactor)
        {
            thresholdFactor = Math.Max(0.0001f, thresholdFactor);
            if (Math.Abs(settings.thresholdFactor - thresholdFactor) <= 0.000001f)
            {
                return;
            }

            settings.thresholdFactor = thresholdFactor;
            foreach (var config in settings.AdvancedConfigs())
            {
                if (config.enabled)
                {
                    config.thresholdFactor = thresholdFactor;
                }
            }

            MarkRuntimeChanged();
        }

        public static void ApplyObjectTargetFilter(
            StatCompressionSettings settings,
            ObjectTargetFilterSettings source)
        {
            settings.ObjectTargetFilter.CopyFrom(source);
            MarkRuntimeChanged();
            CommitPending(settings);
        }

        public static void ApplyPreset(
            StatCompressionSettings settings,
            StatCompressionPreset preset)
        {
            CopyPresetConfigs(settings, preset);

            if (!IsPresetActive(settings, preset.FileName))
            {
                settings.activePresets.Add(preset.FileName);
            }

            MarkActivePresetsChanged();
            CommitPending(settings);
        }

        public static bool ApplyActivePresetUpdate(
            StatCompressionSettings settings,
            StatCompressionPreset preset)
        {
            if (!IsPresetActive(settings, preset.FileName))
            {
                return false;
            }

            CopyPresetConfigs(settings, preset);
            MarkRuntimeChanged();
            CommitPending(settings);
            return true;
        }

        public static void DisablePreset(
            StatCompressionSettings settings,
            StatCompressionPreset preset)
        {
            for (var i = 0; i < preset.Configs.Count; i++)
            {
                var target = settings.GetAdvancedConfig(preset.Configs[i].defName);
                if (target != null)
                {
                    target.enabled = false;
                }
            }

            settings.activePresets.RemoveAll(name =>
                string.Equals(name, preset.FileName, StringComparison.OrdinalIgnoreCase));
            MarkActivePresetsChanged();
            CommitPending(settings);
        }

        public static void CompleteSettingsImport(StatCompressionSettings settings)
        {
            MarkActivePresetsChanged();
            CommitPending(settings);
        }

        public static void ReplaceActivePresets(
            StatCompressionSettings settings,
            IReadOnlyList<string> fileNames)
        {
            settings.activePresets.Clear();
            for (var i = 0; i < fileNames.Count; i++)
            {
                settings.activePresets.Add(fileNames[i]);
            }

            MarkActivePresetsChanged();
        }

        public static void ResetToDefaults(StatCompressionSettings settings)
        {
            settings.ResetToDefaultsData();
            MarkActivePresetsChanged();
            CommitPending(settings);
        }

        public static void MarkRuntimeChanged()
        {
            StatCompressionRuntimeCoordinator.MarkDirty();
        }

        public static void MarkActivePresetsChanged()
        {
            StatCompressionPresetService.NotifyActivePresetsChanged();
            MarkRuntimeChanged();
        }

        public static bool CommitPending(StatCompressionSettings settings)
        {
            return StatCompressionRuntimeCoordinator.ApplyIfDirty(settings);
        }

        private static void CopyPresetConfigs(
            StatCompressionSettings settings,
            StatCompressionPreset preset)
        {
            for (var i = 0; i < preset.Configs.Count; i++)
            {
                var source = preset.Configs[i];
                var target = settings.GetAdvancedConfig(source.defName);
                if (target == null)
                {
                    Verse.Log.Warning(
                        $"[{StatCompressionConstants.DisplayName}] Preset {preset.DisplayName} " +
                        $"skipped missing config {source.defName}.");
                    continue;
                }

                target.CopyFrom(source);
            }
        }

        private static bool IsPresetActive(
            StatCompressionSettings settings,
            string fileName)
        {
            for (var i = 0; i < settings.activePresets.Count; i++)
            {
                if (string.Equals(
                        settings.activePresets[i],
                        fileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

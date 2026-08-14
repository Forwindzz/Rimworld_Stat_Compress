using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace StatCompression
{
    internal readonly struct DefaultPresetSeedResult
    {
        public DefaultPresetSeedResult(int createdCount, bool failed)
        {
            CreatedCount = createdCount;
            Failed = failed;
        }

        public int CreatedCount { get; }
        public bool Failed { get; }
    }

    internal static class StatCompressionDefaultPresetSeeder
    {
        private const string TemplateRelativePath = "Data/DefaultPresets";

        private static readonly string[] DefaultFileNames =
        {
            "default_work_speed",
            "default_armor_penetration_soft_cap",
            "default_base_damage",
            "default_combat_performance",
            "default_psychic",
            "default_daily_needs",
            "default_social_trade_culture",
            "default_production_yield",
            "default_production_yield_soft_cap",
            "default_temperature_insulation",
            "default_movement_speed",
            "default_learning",
            "default_caravan",
            "default_healing_shields_damage_taken"
        };

        public static DefaultPresetSeedResult EnsureLocalTemplates()
        {
            var targetDirectory = StatCompressionPresetManager.UserPresetDirectory;
            try
            {
                if (Directory.Exists(targetDirectory) &&
                    Directory.GetFiles(targetDirectory, "*.xml", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    return new DefaultPresetSeedResult(0, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[{StatCompressionConstants.DisplayName}] Failed to inspect the local preset directory: {ex}");
                return new DefaultPresetSeedResult(0, true);
            }

            var contentPack = StatCompressionMod.ContentPack;
            if (contentPack == null)
            {
                Log.Error($"[{StatCompressionConstants.DisplayName}] Cannot create default presets before ModContentPack is available.");
                return new DefaultPresetSeedResult(0, true);
            }

            var templateDirectory = Path.Combine(
                contentPack.RootDir,
                TemplateRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var sourcePaths = new string[DefaultFileNames.Length];
            var labelKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < DefaultFileNames.Length; i++)
            {
                var path = Path.Combine(templateDirectory, DefaultFileNames[i] + ".xml");
                sourcePaths[i] = path;
                if (!File.Exists(path))
                {
                    Log.Error($"[{StatCompressionConstants.DisplayName}] Missing default preset template: {path}");
                    return new DefaultPresetSeedResult(0, true);
                }

                if (!StatCompressionPresetXml.TryLoad(path, true, out var preset, out var error))
                {
                    Log.Error($"[{StatCompressionConstants.DisplayName}] Invalid default preset template {path}: {error}");
                    return new DefaultPresetSeedResult(0, true);
                }

                if (preset.FileName != DefaultFileNames[i] || preset.LabelKey.NullOrEmpty())
                {
                    Log.Error($"[{StatCompressionConstants.DisplayName}] Default preset template has an invalid identity or missing labelKey: {path}");
                    return new DefaultPresetSeedResult(0, true);
                }

                if (!labelKeys.Add(preset.LabelKey))
                {
                    Log.Error($"[{StatCompressionConstants.DisplayName}] Duplicate default preset labelKey: {preset.LabelKey}");
                    return new DefaultPresetSeedResult(0, true);
                }
            }

            var temporaryPaths = new List<string>(DefaultFileNames.Length);
            var createdPaths = new List<string>(DefaultFileNames.Length);
            try
            {
                Directory.CreateDirectory(targetDirectory);
                for (var i = 0; i < sourcePaths.Length; i++)
                {
                    var temporaryPath = Path.Combine(
                        targetDirectory,
                        "." + DefaultFileNames[i] + ".seed-" + Guid.NewGuid().ToString("N") + ".tmp");
                    File.Copy(sourcePaths[i], temporaryPath, false);
                    temporaryPaths.Add(temporaryPath);
                }

                for (var i = 0; i < temporaryPaths.Count; i++)
                {
                    var targetPath = Path.Combine(targetDirectory, DefaultFileNames[i] + ".xml");
                    if (File.Exists(targetPath))
                    {
                        throw new IOException("Default preset target appeared during initialization: " + targetPath);
                    }

                    File.Move(temporaryPaths[i], targetPath);
                    createdPaths.Add(targetPath);
                }

                return new DefaultPresetSeedResult(createdPaths.Count, false);
            }
            catch (Exception ex)
            {
                for (var i = 0; i < createdPaths.Count; i++)
                {
                    TryDelete(createdPaths[i]);
                }

                Log.Error($"[{StatCompressionConstants.DisplayName}] Failed to create local default presets: {ex}");
                return new DefaultPresetSeedResult(0, true);
            }
            finally
            {
                for (var i = 0; i < temporaryPaths.Count; i++)
                {
                    TryDelete(temporaryPaths[i]);
                }
            }
        }

        public static bool TryApplyDefaults(
            StatCompressionSettings settings,
            out int appliedCount,
            out int skippedMissingConfigs)
        {
            appliedCount = 0;
            skippedMissingConfigs = 0;
            var defaults = new List<StatCompressionPreset>(DefaultFileNames.Length);
            for (var i = 0; i < DefaultFileNames.Length; i++)
            {
                var preset = StatCompressionPresetManager.Find(DefaultFileNames[i]);
                if (preset == null)
                {
                    Log.Error($"[{StatCompressionConstants.DisplayName}] Cannot enable default presets; missing local preset {DefaultFileNames[i]}.");
                    return false;
                }

                defaults.Add(preset);
            }

            if (TryFindConflict(defaults, out var conflict))
            {
                Log.Error(
                    $"[{StatCompressionConstants.DisplayName}] Cannot enable default presets; " +
                    $"{conflict.Left.DisplayName} conflicts with {conflict.Right.DisplayName} " +
                    $"at {conflict.DefName} ({conflict.Fields}).");
                return false;
            }

            settings.activePresets.Clear();
            var missingDefNames = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < defaults.Count; i++)
            {
                var preset = defaults[i];
                for (var j = 0; j < preset.Configs.Count; j++)
                {
                    var source = preset.Configs[j];
                    var target = settings.GetAdvancedConfig(source.defName);
                    if (target == null)
                    {
                        missingDefNames.Add(source.defName);
                        continue;
                    }

                    target.CopyFrom(source);
                }

                settings.activePresets.Add(preset.FileName);
            }

            settings.NormalizeParameters();
            settings.RebuildLookup();
            appliedCount = defaults.Count;
            skippedMissingConfigs = missingDefNames.Count;
            return true;
        }

        public static bool TryReplaceWithDefaults(
            StatCompressionSettings settings,
            out int deletedCount,
            out int appliedCount,
            out int skippedMissingConfigs,
            out string error)
        {
            deletedCount = 0;
            appliedCount = 0;
            skippedMissingConfigs = 0;
            error = null;
            try
            {
                var directory = StatCompressionPresetManager.UserPresetDirectory;
                if (Directory.Exists(directory))
                {
                    var paths = Directory.GetFiles(
                        directory,
                        "*.xml",
                        SearchOption.TopDirectoryOnly);
                    for (var i = 0; i < paths.Length; i++)
                    {
                        File.Delete(paths[i]);
                        deletedCount++;
                    }
                }

                var seedResult = EnsureLocalTemplates();
                if (seedResult.Failed)
                {
                    error = "Failed to recreate the bundled default preset files.";
                    return false;
                }

                StatCompressionPresetManager.Refresh();
                if (!TryApplyDefaults(
                        settings,
                        out appliedCount,
                        out skippedMissingConfigs))
                {
                    error = "Failed to apply the bundled default presets.";
                    return false;
                }

                var defaultDefNames = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < DefaultFileNames.Length; i++)
                {
                    var preset = StatCompressionPresetManager.Find(DefaultFileNames[i]);
                    for (var j = 0; j < preset.Configs.Count; j++)
                    {
                        defaultDefNames.Add(preset.Configs[j].defName);
                    }
                }

                foreach (var config in settings.AdvancedConfigs())
                {
                    if (!defaultDefNames.Contains(config.defName))
                    {
                        config.enabled = false;
                    }
                }

                settings.RebuildLookup();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Log.Error(
                    $"[{StatCompressionConstants.DisplayName}] Failed to replace local presets with defaults: {ex}");
                return false;
            }
        }

        private static bool TryFindConflict(
            IReadOnlyList<StatCompressionPreset> presets,
            out DefaultPresetConflict conflict)
        {
            for (var i = 0; i < presets.Count; i++)
            {
                var left = presets[i];
                var leftByDefName = left.Configs.ToDictionary(
                    config => config.defName,
                    StringComparer.Ordinal);
                for (var j = i + 1; j < presets.Count; j++)
                {
                    var right = presets[j];
                    for (var k = 0; k < right.Configs.Count; k++)
                    {
                        var rightConfig = right.Configs[k];
                        if (!leftByDefName.TryGetValue(rightConfig.defName, out var leftConfig))
                        {
                            continue;
                        }

                        var fields = StatCompressionPresetManager.DifferentFields(leftConfig, rightConfig);
                        if (fields.Length == 0)
                        {
                            continue;
                        }

                        conflict = new DefaultPresetConflict(
                            left,
                            right,
                            rightConfig.defName,
                            string.Join(", ", fields));
                        return true;
                    }
                }
            }

            conflict = default;
            return false;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private readonly struct DefaultPresetConflict
        {
            public DefaultPresetConflict(
                StatCompressionPreset left,
                StatCompressionPreset right,
                string defName,
                string fields)
            {
                Left = left;
                Right = right;
                DefName = defName;
                Fields = fields;
            }

            public StatCompressionPreset Left { get; }
            public StatCompressionPreset Right { get; }
            public string DefName { get; }
            public string Fields { get; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionPresetManager
    {
        private const float FloatTolerance = 0.000001f;
        private static readonly List<StatCompressionPreset> presets = new List<StatCompressionPreset>();
        private static bool loaded;

        public static IReadOnlyList<StatCompressionPreset> Presets
        {
            get
            {
                EnsureLoaded();
                return presets;
            }
        }

        public static string UserPresetDirectory =>
            Path.Combine(GenFilePaths.ConfigFolderPath, "StatCompression", "Presets");

        public static void Refresh()
        {
            loaded = true;
            presets.Clear();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var contentPack = StatCompressionMod.ContentPack;
            if (contentPack != null)
            {
                LoadDirectory(Path.Combine(contentPack.RootDir, "Data", "Presets"), true, names);
            }

            LoadDirectory(UserPresetDirectory, false, names);
            presets.Sort((left, right) =>
                string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        }

        public static StatCompressionPreset Find(string fileName)
        {
            EnsureLoaded();
            return presets.FirstOrDefault(preset =>
                string.Equals(preset.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        }

        public static StatCompressionPreset Clone(StatCompressionPreset source)
        {
            return new StatCompressionPreset
            {
                Name = source.Name,
                LabelKey = source.LabelKey,
                FileName = source.FileName,
                Path = source.Path,
                BuiltIn = source.BuiltIn,
                Configs = source.Configs.Select(CloneConfig).ToList()
            };
        }

        public static bool TryCreate(
            string name,
            IEnumerable<StatCompressionStatConfig> configs,
            out StatCompressionPreset preset,
            out string error)
        {
            EnsureLoaded();
            preset = null;
            error = null;
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                error = StatCompressionText.T("StatCompression_Preset_ErrorEmptyName");
                return false;
            }

            var fileName = SafeFileName(name);
            if (fileName.Length == 0)
            {
                error = StatCompressionText.T("StatCompression_Preset_ErrorInvalidName");
                return false;
            }

            if (Find(fileName) != null)
            {
                error = StatCompressionText.T("StatCompression_Preset_ErrorExists", fileName);
                return false;
            }

            var copiedConfigs = configs
                .Where(config => config != null)
                .GroupBy(config => config.defName, StringComparer.Ordinal)
                .Select(group => CloneConfig(group.First()))
                .OrderBy(config => config.defName, StringComparer.Ordinal)
                .ToList();
            if (copiedConfigs.Count == 0)
            {
                error = StatCompressionText.T("StatCompression_Preset_ErrorNoSelection");
                return false;
            }

            try
            {
                Directory.CreateDirectory(UserPresetDirectory);
                var path = Path.Combine(UserPresetDirectory, fileName + ".xml");
                preset = new StatCompressionPreset
                {
                    Name = name,
                    FileName = fileName,
                    Path = path,
                    Configs = copiedConfigs
                };
                StatCompressionPresetXml.Save(preset, path);
                Refresh();
                preset = Find(fileName);
                return true;
            }
            catch (Exception ex)
            {
                preset = null;
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public static bool TrySave(StatCompressionPreset preset, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(preset.Path));
                StatCompressionPresetXml.Save(preset, preset.Path);
                Refresh();
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public static bool TryGetImportCollision(
            StatCompressionPreset source,
            out StatCompressionPreset existing,
            out string error)
        {
            EnsureLoaded();
            existing = null;
            if (!TryGetImportFileName(source, out var fileName, out error))
            {
                return false;
            }

            existing = presets.FirstOrDefault(preset =>
                preset.BuiltIn &&
                string.Equals(
                    preset.DisplayName,
                    source.DisplayName,
                    StringComparison.CurrentCultureIgnoreCase));
            if (existing == null)
            {
                existing = Find(fileName);
            }
            if (existing != null && existing.BuiltIn)
            {
                error = StatCompressionText.T(
                    "StatCompression_PresetBuiltinConflict",
                    existing.DisplayName);
                return false;
            }

            return true;
        }

        public static bool TryImport(
            StatCompressionPreset source,
            bool overwrite,
            out StatCompressionPreset imported,
            out string error)
        {
            imported = null;
            if (!TryGetImportCollision(source, out var existing, out error))
            {
                return false;
            }
            if (existing != null && !overwrite)
            {
                error = StatCompressionText.T(
                    "StatCompression_Preset_ErrorExists",
                    existing.DisplayName);
                return false;
            }

            var fileName = SafeFileName(source.Name);
            var path = Path.Combine(UserPresetDirectory, fileName + ".xml");
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(UserPresetDirectory);
                var copy = new StatCompressionPreset
                {
                    Name = source.Name.Trim(),
                    LabelKey = source.LabelKey,
                    FileName = fileName,
                    Path = path,
                    BuiltIn = false,
                    Configs = source.Configs
                        .Where(config => config != null)
                        .Select(CloneConfig)
                        .OrderBy(config => config.defName, StringComparer.Ordinal)
                        .ToList()
                };
                StatCompressionPresetXml.Save(copy, temporaryPath);
                if (File.Exists(path))
                {
                    File.Copy(temporaryPath, path, true);
                    File.Delete(temporaryPath);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }

                Refresh();
                imported = Find(fileName);
                return imported != null;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static bool TryFindConflict(
            StatCompressionSettings settings,
            StatCompressionPreset candidate,
            out StatCompressionPresetConflict conflict)
        {
            EnsureLoaded();
            for (var i = 0; i < settings.activePresets.Count; i++)
            {
                var activeName = settings.activePresets[i];
                if (string.Equals(activeName, candidate.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var active = Find(activeName);
                if (active == null)
                {
                    continue;
                }

                var activeByName = active.Configs.ToDictionary(config => config.defName, StringComparer.Ordinal);
                for (var j = 0; j < candidate.Configs.Count; j++)
                {
                    var config = candidate.Configs[j];
                    if (activeByName.TryGetValue(config.defName, out var other))
                    {
                        var fields = DifferentFields(config, other);
                        if (fields.Length > 0)
                        {
                            conflict = new StatCompressionPresetConflict
                            {
                                PresetName = active.DisplayName,
                                DefName = config.defName,
                                Fields = string.Join(", ", fields)
                            };
                            return true;
                        }
                    }
                }
            }

            conflict = null;
            return false;
        }

        public static void Apply(StatCompressionSettings settings, StatCompressionPreset preset)
        {
            for (var i = 0; i < preset.Configs.Count; i++)
            {
                var source = preset.Configs[i];
                var target = settings.GetAdvancedConfig(source.defName);
                if (target == null)
                {
                    Log.Warning($"[{StatCompressionConstants.DisplayName}] Preset {preset.DisplayName} skipped missing config {source.defName}.");
                    continue;
                }

                target.CopyFrom(source);
                StatCompressionSettings.NormalizeConfig(target);
            }

            if (!settings.activePresets.Any(name =>
                    string.Equals(name, preset.FileName, StringComparison.OrdinalIgnoreCase)))
            {
                settings.activePresets.Add(preset.FileName);
            }

            settings.NormalizeParameters();
            settings.RebuildLookup();
        }

        public static void Disable(StatCompressionSettings settings, StatCompressionPreset preset)
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
            settings.RebuildLookup();
        }

        private static void EnsureLoaded()
        {
            if (!loaded)
            {
                Refresh();
            }
        }

        private static void LoadDirectory(string directory, bool builtIn, HashSet<string> names)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.GetFiles(directory, "*.xml", SearchOption.TopDirectoryOnly))
            {
                if (!StatCompressionPresetXml.TryLoad(path, builtIn, out var preset, out var error))
                {
                    Log.Warning($"[{StatCompressionConstants.DisplayName}] Invalid preset XML {path}: {error}");
                    continue;
                }

                if (!names.Add(preset.FileName))
                {
                    Log.Warning($"[{StatCompressionConstants.DisplayName}] Duplicate preset file name ignored: {preset.FileName}");
                    continue;
                }

                presets.Add(preset);
            }
        }

        private static StatCompressionStatConfig CloneConfig(StatCompressionStatConfig source)
        {
            var clone = new StatCompressionStatConfig();
            clone.CopyFrom(source);
            return clone;
        }

        internal static string[] DifferentFields(
            StatCompressionStatConfig left,
            StatCompressionStatConfig right)
        {
            var fields = new List<string>();
            if (left.enabled != right.enabled) fields.Add("enabled");
            if (left.method != right.method) fields.Add("method");
            if (!NearlyEqual(left.method_t, right.method_t)) fields.Add("method_t");
            if (!NearlyEqual(left.tScale, right.tScale)) fields.Add("tScale");
            if (!NearlyEqual(left.baseline, right.baseline)) fields.Add("baseline");
            if (!NearlyEqual(left.thresholdFactor, right.thresholdFactor)) fields.Add("thresholdFactor");
            if (left.direction != right.direction) fields.Add("direction");
            return fields.ToArray();
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= FloatTolerance;
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        }

        private static bool TryGetImportFileName(
            StatCompressionPreset source,
            out string fileName,
            out string error)
        {
            fileName = string.Empty;
            error = null;
            if (source == null || source.Name.NullOrEmpty())
            {
                error = StatCompressionText.T("StatCompression_Preset_ErrorEmptyName");
                return false;
            }

            fileName = SafeFileName(source.Name.Trim());
            if (fileName.Length == 0)
            {
                error = StatCompressionText.T("StatCompression_Preset_ErrorInvalidName");
                return false;
            }
            if (source.Configs == null || source.Configs.Count == 0)
            {
                error = StatCompressionText.T("StatCompression_Preset_ErrorNoSelection");
                return false;
            }

            return true;
        }
    }
}

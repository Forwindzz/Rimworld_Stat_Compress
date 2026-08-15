using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace StatCompression
{
    public sealed partial class StatCompressionSettings
    {
        public string ExportSettingsToXml()
        {
            EnsureStatConfigs();

            var dir = Path.Combine(GenFilePaths.ConfigFolderPath, "StatCompression");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.xml");

            StatCompressionSettingsXml.CreateDocument(this).Save(path);
            Log.Message($"[{StatCompressionConstants.DisplayName}] Exported settings XML to {path}");
            return path;
        }

        public string ImportSettingsFromXml(out int updated, out int skipped)
        {
            return ImportSettingsFromXml(out updated, out skipped, out _);
        }

        public string ImportSettingsFromXml(
            out int updated,
            out int skipped,
            out string error)
        {
            EnsureStatConfigs();
            var path = Path.Combine(GenFilePaths.ConfigFolderPath, "StatCompression", "settings.xml");
            updated = 0;
            skipped = 0;
            error = null;

            if (!File.Exists(path))
            {
                error = $"Settings XML import file not found: {path}";
                Log.Warning($"[{StatCompressionConstants.DisplayName}] {error}");
                return path;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(path);
            }
            catch (Exception ex)
            {
                error = $"Failed to read settings XML: {ex.GetType().Name}: {ex.Message}";
                Log.Warning($"[{StatCompressionConstants.DisplayName}] {error}: {path}\n{ex}");
                return path;
            }

            if (!StatCompressionSettingsXml.TryGetRoot(document, out var root, out var rootError))
            {
                error = $"Invalid settings XML: {rootError}";
                Log.Warning($"[{StatCompressionConstants.DisplayName}] {error}: {path}");
                return path;
            }

            var importedSchemaVersion = StatCompressionSettingsXml.VersionOf(root);

            var importedGlobal = new DefaultGlobalSettings
            {
                enabled = enabled,
                showInfoCardSettingsButton = showInfoCardSettingsButton,
                stage = stage,
                autoFallbackToGlobalPostfix = autoFallbackToGlobalPostfix,
                method = method,
                parameter = parameter,
                thresholdFactor = thresholdFactor
            };
            StatCompressionSettingsXml.ReadGlobal(root.Element("Global"), importedGlobal);
            var importedObjectTargetFilter = new ObjectTargetFilterSettings();
            StatCompressionSettingsXml.ReadObjectTargetFilter(
                root.Element("ObjectTargetFilter"),
                importedObjectTargetFilter);
            StatCompressionSettingsXml.ReadBodyPartHealth(root.Element("BodyPartHealth"), BodyPartHealthConfig);
            EnsureSpecialDamageConfigs();
            updated += StatCompressionSettingsXml.ReadSpecialDamageConfigs(
                root.Element("SpecialDamageConfigs"),
                specialDamageConfigs);
            EnsureSpecialHediffStageConfigs();
            updated += StatCompressionSettingsXml.ReadSpecialHediffStageConfigs(
                root.Element("SpecialHediffStageConfigs"),
                specialHediffStageConfigs);
            enabled = importedGlobal.enabled;
            showInfoCardSettingsButton = importedGlobal.showInfoCardSettingsButton;
            stage = importedGlobal.stage;
            autoFallbackToGlobalPostfix = importedGlobal.autoFallbackToGlobalPostfix;
            method = importedGlobal.method;
            parameter = importedGlobal.parameter;
            thresholdFactor = importedGlobal.thresholdFactor;
            objectTargetFilter = importedObjectTargetFilter;
            activePresets = StatCompressionSettingsXml.ReadActivePresets(root.Element("ActivePresets"));

            var existing = statConfigs
                .Where(config => config != null && !config.defName.NullOrEmpty())
                .GroupBy(config => config.defName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var statsElement = root.Element("Stats");
            if (statsElement != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var warnedDuplicates = new HashSet<string>(StringComparer.Ordinal);
                foreach (var statElement in statsElement.Elements("Stat"))
                {
                    var defName = StatCompressionSettingsXml.Attr(statElement, "defName");
                    if (defName.NullOrEmpty())
                    {
                        skipped++;
                        continue;
                    }

                    if (!seen.Add(defName))
                    {
                        skipped++;
                        if (warnedDuplicates.Add(defName))
                        {
                            Log.Warning($"[{StatCompressionConstants.DisplayName}] Duplicate imported StatDef ignored: {defName}");
                        }

                        continue;
                    }

                    var addedUnknown = false;
                    if (!existing.TryGetValue(defName, out var config))
                    {
                        config = new StatCompressionStatConfig(
                            defName,
                            false,
                            CompressionMethod.FollowGlobal,
                            importedGlobal.parameter,
                            1f,
                            1f,
                            importedGlobal.thresholdFactor,
                            StatCompressionDirection.HigherIsBetter);
                        existing.Add(defName, config);
                        statConfigs.Add(config);
                        addedUnknown = true;
                    }

                    try
                    {
                        StatCompressionSettingsXml.ApplyStatElement(statElement, config);
                        MigrateLegacyImportedMethod(config, importedGlobal.method, importedSchemaVersion);
                        NormalizeConfig(config);
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        if (addedUnknown)
                        {
                            existing.Remove(defName);
                            statConfigs.Remove(config);
                        }
                        skipped++;
                        Log.Warning(
                            $"[{StatCompressionConstants.DisplayName}] Skipped XML config for {defName}: " +
                            $"{ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            if (importedSchemaVersion < 2)
            {
                MigrateLegacyImportedElement(
                    root.Element("BodyPartHealth"),
                    BodyPartHealthConfig,
                    importedGlobal.method);
                MigrateLegacyImportedSpecialElements(
                    root.Element("SpecialDamageConfigs"),
                    specialDamageConfigs,
                    importedGlobal.method);
                MigrateLegacyImportedSpecialElements(
                    root.Element("SpecialHediffStageConfigs"),
                    specialHediffStageConfigs,
                    importedGlobal.method);
            }

            EnsureInvariantsAfterLoadOrImport();
            Log.Message($"[{StatCompressionConstants.DisplayName}] Imported settings XML: updated={updated}, skipped={skipped}, path={path}");
            return path;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace StatCompression
{
    internal static class StatCompressionPresetXml
    {
        private const string RootName = "StatCompressionPreset";

        public static bool TryLoad(string path, bool builtIn, out StatCompressionPreset preset, out string error)
        {
            preset = null;
            error = null;
            try
            {
                var document = XDocument.Load(path);
                return TryReadDocument(
                    document,
                    System.IO.Path.GetFileNameWithoutExtension(path),
                    path,
                    builtIn,
                    out preset,
                    out error);
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public static bool TryParse(
            string xml,
            out StatCompressionPreset preset,
            out string error)
        {
            preset = null;
            error = null;
            if (xml.NullOrEmpty())
            {
                error = "clipboard is empty";
                return false;
            }

            try
            {
                return TryReadDocument(
                    XDocument.Parse(xml),
                    null,
                    null,
                    false,
                    out preset,
                    out error);
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public static XDocument CreateDocument(StatCompressionPreset preset)
        {
            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(
                    RootName,
                    new XAttribute("name", preset.Name),
                    new XElement(
                        "Configs",
                        preset.Configs
                            .Where(config => config != null && !config.defName.NullOrEmpty())
                            .OrderBy(config => config.defName, StringComparer.Ordinal)
                            .Select(config => StatCompressionSettingsXml.CreateConfigElement("Config", config)))));
        }

        public static void Save(StatCompressionPreset preset, string path)
        {
            CreateDocument(preset).Save(path);
        }

        private static bool TryReadDocument(
            XDocument document,
            string fallbackName,
            string path,
            bool builtIn,
            out StatCompressionPreset preset,
            out string error)
        {
            preset = null;
            error = null;
            var root = document.Root;
            if (root == null || root.Name != RootName)
            {
                error = $"expected root {RootName}";
                return false;
            }

            var name = root.Attribute("name")?.Value?.Trim();
            if (name.NullOrEmpty())
            {
                name = fallbackName;
            }
            if (name.NullOrEmpty())
            {
                error = "preset name is empty";
                return false;
            }

            var configsElement = root.Element("Configs");
            if (configsElement == null)
            {
                error = "missing Configs element";
                return false;
            }

            var configs = new List<StatCompressionStatConfig>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in configsElement.Elements("Config"))
            {
                StatCompressionStatConfig config;
                if (!StatCompressionSettingsXml.TryReadConfig(element, out config, out error))
                {
                    return false;
                }

                if (!seen.Add(config.defName))
                {
                    error = $"duplicate config {config.defName}";
                    return false;
                }

                configs.Add(config);
            }

            if (configs.Count == 0)
            {
                error = "preset contains no configs";
                return false;
            }

            preset = new StatCompressionPreset
            {
                Name = name,
                FileName = fallbackName,
                Path = path,
                BuiltIn = builtIn,
                Configs = configs.OrderBy(config => config.defName, StringComparer.Ordinal).ToList()
            };
            return true;
        }
    }
}

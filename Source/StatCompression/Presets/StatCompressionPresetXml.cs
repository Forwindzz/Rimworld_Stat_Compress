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
                var root = document.Root;
                if (root == null || root.Name != RootName)
                {
                    error = $"expected root {RootName}";
                    return false;
                }

                var name = root.Attribute("name")?.Value;
                if (name.NullOrEmpty())
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(path);
                }

                var configs = new List<StatCompressionStatConfig>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var configsElement = root.Element("Configs");
                if (configsElement != null)
                {
                    foreach (var element in configsElement.Elements("Config"))
                    {
                        if (!StatCompressionSettingsXml.TryReadConfig(element, out var config, out error))
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
                }

                preset = new StatCompressionPreset
                {
                    Name = name,
                    FileName = System.IO.Path.GetFileNameWithoutExtension(path),
                    Path = path,
                    BuiltIn = builtIn,
                    Configs = configs.OrderBy(config => config.defName, StringComparer.Ordinal).ToList()
                };
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public static void Save(StatCompressionPreset preset, string path)
        {
            var document = new XDocument(
                new XElement(
                    RootName,
                    new XAttribute("name", preset.Name),
                    new XElement(
                        "Configs",
                        preset.Configs
                            .Where(config => config != null && !config.defName.NullOrEmpty())
                            .OrderBy(config => config.defName, StringComparer.Ordinal)
                            .Select(config => StatCompressionSettingsXml.CreateConfigElement("Config", config)))));
            document.Save(path);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class BaseDamageReportCache
    {
        private static readonly AccessTools.FieldRef<StatDrawEntry, string> OverrideReportText =
            AccessTools.FieldRefAccess<StatDrawEntry, string>("overrideReportText");

        private static readonly AccessTools.FieldRef<StatDrawEntry, string> ExplanationText =
            AccessTools.FieldRefAccess<StatDrawEntry, string>("explanationText");

        private static readonly Dictionary<Def, List<OwnerReport>> ownerReports =
            new Dictionary<Def, List<OwnerReport>>(BaseDamageReferenceComparer<Def>.Instance);

        private static readonly Dictionary<ThingDef, List<ProjectileReport>> projectileReports =
            new Dictionary<ThingDef, List<ProjectileReport>>(
                BaseDamageReferenceComparer<ThingDef>.Instance);

        public static void Clear()
        {
            ownerReports.Clear();
            projectileReports.Clear();
        }

        public static void Rebuild(StatCompressionSettings settings)
        {
            Clear();
            foreach (var pair in BaseDamageDefStore.OwnerRecords)
            {
                BuildOwnerReports(pair.Key, pair.Value, settings);
                if (pair.Key is ThingDef thingDef)
                {
                    BuildProjectileReports(thingDef, settings);
                }
            }
        }

        public static IEnumerable<StatDrawEntry> AppendInfoEntries(
            IEnumerable<StatDrawEntry> original,
            Def owner)
        {
            foreach (var entry in original)
            {
                yield return entry;
            }

            if (owner == null || !ownerReports.TryGetValue(owner, out var reports))
            {
                yield break;
            }

            for (var i = 0; i < reports.Count; i++)
            {
                var report = reports[i];
                yield return new StatDrawEntry(
                    report.StatCategory,
                    report.Label,
                    report.Value,
                    report.ReportText,
                    5495,
                    null,
                    null,
                    false,
                    false);
            }
        }

        public static IEnumerable<StatDrawEntry> AppendThingDefDamageReports(
            IEnumerable<StatDrawEntry> original,
            ThingDef owner)
        {
            projectileReports.TryGetValue(owner, out var reports);
            var projectileIndex = 0;
            foreach (var entry in original)
            {
                if (entry.DisplayPriorityWithinCategory == 5500 &&
                    reports != null &&
                    projectileIndex < reports.Count)
                {
                    var report = reports[projectileIndex++];
                    if (!report.CompressionDetails.NullOrEmpty())
                    {
                        var originalReport = OverrideReportText(entry);
                        entry.SetReportText(
                            originalReport.NullOrEmpty()
                                ? report.CompressionDetails
                                : originalReport.TrimEndNewlines() + "\n\n" +
                                  report.CompressionDetails);
                        ExplanationText(entry) = null;
                    }
                }

                yield return entry;
            }
        }

        private static void BuildOwnerReports(
            Def owner,
            List<DamageFieldRecord> records,
            StatCompressionSettings settings)
        {
            var reports = new List<OwnerReport>();
            foreach (var group in records
                         .Where(record => record.Changed)
                         .GroupBy(record => record.Category)
                         .OrderBy(group => group.Key))
            {
                var changed = group
                    .Distinct(BaseDamageReferenceComparer<DamageFieldRecord>.Instance)
                    .ToList();
                if (changed.Count == 0)
                {
                    continue;
                }

                var config = ConfigFor(settings, group.Key);
                reports.Add(new OwnerReport
                {
                    StatCategory = CategoryFor(group.Key),
                    Label = SpecialCompressionConfigs.LabelFor(config.defName),
                    Value = string.Join(
                        " / ",
                        changed.Select(record => FormatDamage(record.Applied)).Distinct()),
                    ReportText = BuildReport(changed, config, settings)
                });
            }

            if (reports.Count > 0)
            {
                ownerReports.Add(owner, reports);
            }
        }

        private static void BuildProjectileReports(
            ThingDef owner,
            StatCompressionSettings settings)
        {
            var records = BaseDamageDefStore.ProjectileRecordsFor(owner);
            if (records.Count == 0)
            {
                return;
            }

            var reports = new List<ProjectileReport>(records.Count);
            for (var i = 0; i < records.Count; i++)
            {
                var record = records[i];
                reports.Add(new ProjectileReport
                {
                    CompressionDetails = record.Changed
                        ? BuildCompressionDetails(
                            new List<DamageFieldRecord> { record },
                            ConfigFor(settings, record.Category),
                            settings)
                        : null
                });
            }

            projectileReports.Add(owner, reports);
        }

        private static StatCompressionStatConfig ConfigFor(
            StatCompressionSettings settings,
            BaseDamageCategory category)
        {
            return settings.GetAdvancedConfig(
                SpecialCompressionConfigs.DamageDefNames[(int)category]);
        }

        private static StatCategoryDef CategoryFor(BaseDamageCategory category)
        {
            switch (category)
            {
                case BaseDamageCategory.MeleeBase:
                case BaseDamageCategory.MeleeExtra:
                    return StatCategoryDefOf.Weapon_Melee;
                case BaseDamageCategory.RangedBase:
                case BaseDamageCategory.RangedExtra:
                    return StatCategoryDefOf.Weapon_Ranged;
                default:
                    return StatCategoryDefOf.Basics;
            }
        }

        private static string BuildReport(
            List<DamageFieldRecord> records,
            StatCompressionStatConfig config,
            StatCompressionSettings settings)
        {
            return "Stat_Thing_Damage_Desc".Translate() + "\n\n" +
                   BuildCompressionDetails(records, config, settings);
        }

        private static string BuildCompressionDetails(
            List<DamageFieldRecord> records,
            StatCompressionStatConfig config,
            StatCompressionSettings settings)
        {
            var parameter = StatCompressionRuntime.GetActualParameter(
                config.method,
                settings.method,
                settings.parameter,
                config.tScale);
            var actualMethod = StatCompressionRuntime.ResolveMethod(
                config.method,
                settings.method);
            var lines = new List<string>
            {
                "<color=#A0A0A0>",
                StatCompressionText.T("StatCompression_Explanation_Separator"),
                StatCompressionText.T("StatCompression_BaseDamage_Info_Header")
            };

            for (var i = 0; i < records.Count; i++)
            {
                lines.Add(StatCompressionText.T(
                    "StatCompression_BaseDamage_Info_Value",
                    records[i].SourceLabel,
                    FormatDamage(records[i].Original),
                    FormatDamage(records[i].Applied)));
            }

            lines.Add(StatCompressionText.T(
                "StatCompression_BaseDamage_Info_Method",
                StatCompressionText.MethodLabel(actualMethod),
                parameter.ToString("0.###"),
                FormatDamage(config.baseline),
                (config.thresholdFactor * 100f).ToString("0.###") + "%"));
            lines.Add(StatCompressionText.T("StatCompression_BaseDamage_Info_AfterFactors"));
            lines.Add("</color>");
            return string.Join("\n", lines);
        }

        private static string FormatDamage(float value)
        {
            return value.ToString("0.###");
        }

        private sealed class OwnerReport
        {
            public StatCategoryDef StatCategory;
            public string Label;
            public string Value;
            public string ReportText;
        }

        private sealed class ProjectileReport
        {
            public string CompressionDetails;
        }
    }
}

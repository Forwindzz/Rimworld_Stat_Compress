using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal enum BaseDamageCategory : byte
    {
        MeleeBase,
        RangedBase,
        MeleeExtra,
        RangedExtra,
        Explosion,
        Other
    }

    internal static class BaseDamageCompressionModule
    {
        private static readonly AccessTools.FieldRef<ProjectileProperties, int> ProjectileDamage =
            AccessTools.FieldRefAccess<ProjectileProperties, int>("damageAmountBase");

        private static readonly AccessTools.FieldRef<ProjectileProperties, float> ProjectileArmorPenetration =
            AccessTools.FieldRefAccess<ProjectileProperties, float>("armorPenetrationBase");

        private static readonly AccessTools.FieldRef<StatDrawEntry, string> OverrideReportText =
            AccessTools.FieldRefAccess<StatDrawEntry, string>("overrideReportText");

        private static readonly AccessTools.FieldRef<StatDrawEntry, string> ExplanationText =
            AccessTools.FieldRefAccess<StatDrawEntry, string>("explanationText");

        private static readonly Dictionary<Def, List<DamageFieldRecord>> RecordsByOwner =
            new Dictionary<Def, List<DamageFieldRecord>>(ReferenceComparer<Def>.Instance);

        private static readonly List<DamageFieldRecord> Records = new List<DamageFieldRecord>();
        private static bool initialized;

        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            try
            {
                ScanDefs();
                Rebuild(StatCompressionMod.Settings);
                Log.Message(
                    $"[{StatCompressionConstants.DisplayName}] Base-damage Def module initialized: " +
                    $"fields={Records.Count}, owners={RecordsByOwner.Count}, changed={CountChanged()}.");
            }
            catch (Exception ex)
            {
                RestoreAll();
                RefreshDerivedStats();
                Log.Error(
                    $"[{StatCompressionConstants.DisplayName}] Failed to initialize base-damage Def compression. " +
                    $"All captured fields were restored.\n{ex}");
            }
        }

        public static void NotifySettingsChanged(StatCompressionSettings settings)
        {
            if (!initialized)
            {
                return;
            }

            RestoreAll();
            ScanDefs();
            Rebuild(settings);
        }

        public static IEnumerable<StatDrawEntry> AppendInfoEntries(
            IEnumerable<StatDrawEntry> original,
            Def owner)
        {
            foreach (var entry in original)
            {
                yield return entry;
            }

            if (owner == null || !RecordsByOwner.TryGetValue(owner, out var records))
            {
                yield break;
            }

            foreach (var group in records
                         .Where(record => record.Changed)
                         .GroupBy(record => record.Category)
                         .OrderBy(group => group.Key))
            {
                var changed = group.Distinct(ReferenceComparer<DamageFieldRecord>.Instance).ToList();
                if (changed.Count == 0)
                {
                    continue;
                }

                var config = ConfigFor(StatCompressionMod.Settings, group.Key);
                var value = string.Join(
                    " / ",
                    changed
                        .Select(record => FormatDamage(record.Applied))
                        .Distinct());
                yield return new StatDrawEntry(
                    CategoryFor(group.Key),
                    SpecialCompressionConfigs.LabelFor(config.defName),
                    value,
                    BuildReport(changed, config),
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
            var projectileRecords = ProjectileRecordsFor(owner);
            var projectileIndex = 0;
            foreach (var entry in original)
            {
                if (entry.DisplayPriorityWithinCategory == 5500 &&
                    projectileIndex < projectileRecords.Count)
                {
                    var record = projectileRecords[projectileIndex++];
                    if (record.Changed)
                    {
                        var config = ConfigFor(StatCompressionMod.Settings, record.Category);
                        var originalReport = OverrideReportText(entry);
                        var compressionReport = BuildCompressionDetails(
                            new List<DamageFieldRecord> { record },
                            config);
                        entry.SetReportText(
                            originalReport.NullOrEmpty()
                                ? compressionReport
                                : originalReport.TrimEndNewlines() + "\n\n" + compressionReport);
                        ExplanationText(entry) = null;
                    }
                }

                yield return entry;
            }
        }

        private static List<ProjectileDamageRecord> ProjectileRecordsFor(ThingDef owner)
        {
            var result = new List<ProjectileDamageRecord>();
            if (owner == null ||
                !RecordsByOwner.TryGetValue(owner, out var ownerRecords) ||
                owner.Verbs.NullOrEmpty())
            {
                return result;
            }

            for (var i = 0; i < owner.Verbs.Count; i++)
            {
                var projectile = owner.Verbs[i]?.defaultProjectile?.projectile;
                if (projectile?.damageDef == null || !projectile.damageDef.harmsHealth)
                {
                    continue;
                }

                var record = ownerRecords
                    .OfType<ProjectileDamageRecord>()
                    .FirstOrDefault(candidate => ReferenceEquals(candidate.Target, projectile));
                if (record != null)
                {
                    result.Add(record);
                }
            }

            return result;
        }

        private static void Rebuild(StatCompressionSettings settings)
        {
            RestoreAll();
            if (settings == null || !settings.enabled)
            {
                RefreshDerivedStats();
                return;
            }

            var compiled = new CompiledStatConfig[SpecialCompressionConfigs.DamageDefNames.Length];
            for (var i = 0; i < compiled.Length; i++)
            {
                compiled[i] = StatCompressionRuntimeCompiler.CompileConfig(
                    settings,
                    settings.GetAdvancedConfig(SpecialCompressionConfigs.DamageDefNames[i]));
            }

            for (var i = 0; i < Records.Count; i++)
            {
                ref var config = ref compiled[(int)Records[i].Category];
                Records[i].Apply(ref config);
            }

            RefreshDerivedStats();
        }

        private static void RestoreAll()
        {
            for (var i = 0; i < Records.Count; i++)
            {
                Records[i].Restore();
            }
        }

        private static int CountChanged()
        {
            var count = 0;
            for (var i = 0; i < Records.Count; i++)
            {
                if (Records[i].Changed)
                {
                    count++;
                }
            }

            return count;
        }

        private static void ScanDefs()
        {
            Records.Clear();
            RecordsByOwner.Clear();
            var collector = new Collector();

            foreach (var thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                collector.AddTools(thingDef.tools, thingDef);
                collector.AddVerbs(thingDef.Verbs, thingDef);
                if (thingDef.projectile != null)
                {
                    collector.AddProjectile(thingDef, thingDef);
                }

                if (thingDef.comps != null)
                {
                    for (var i = 0; i < thingDef.comps.Count; i++)
                    {
                        collector.AddThingComp(thingDef.comps[i], thingDef);
                    }
                }

            }

            foreach (var hediffDef in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                if (hediffDef.comps == null)
                {
                    continue;
                }

                for (var i = 0; i < hediffDef.comps.Count; i++)
                {
                    if (hediffDef.comps[i] is HediffCompProperties_VerbGiver verbGiver)
                    {
                        collector.AddTools(verbGiver.tools, hediffDef);
                        collector.AddVerbs(verbGiver.verbs, hediffDef);
                    }
                    else if (hediffDef.comps[i] is HediffCompProperties_ExplodeOnDeath explodeOnDeath)
                    {
                        collector.AddExplodeOnDeath(explodeOnDeath, hediffDef);
                    }
                }
            }

            foreach (var abilityDef in DefDatabase<AbilityDef>.AllDefsListForReading)
            {
                collector.AddVerb(abilityDef.verbProperties, abilityDef);
                if (abilityDef.comps == null)
                {
                    continue;
                }

                for (var i = 0; i < abilityDef.comps.Count; i++)
                {
                    if (abilityDef.comps[i] is CompProperties_AbilityExplosion explosion)
                    {
                        collector.AddAbilityExplosion(explosion, abilityDef);
                    }
                }
            }

            foreach (var terrainDef in DefDatabase<TerrainDef>.AllDefsListForReading)
            {
                collector.AddTools(terrainDef.tools, terrainDef);
            }

            foreach (var mutantDef in DefDatabase<MutantDef>.AllDefsListForReading)
            {
                collector.AddTools(mutantDef.tools, mutantDef);
                collector.AddVerbs(mutantDef.verbs, mutantDef);
            }

            foreach (var traitDef in DefDatabase<WeaponTraitDef>.AllDefsListForReading)
            {
                collector.AddExtras(traitDef.extraDamages, BaseDamageCategory.RangedExtra, traitDef, traitDef.LabelCap);
            }
        }

        private static StatCompressionStatConfig ConfigFor(
            StatCompressionSettings settings,
            BaseDamageCategory category)
        {
            return settings.GetAdvancedConfig(SpecialCompressionConfigs.DamageDefNames[(int)category]);
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
            StatCompressionStatConfig config)
        {
            return "Stat_Thing_Damage_Desc".Translate() + "\n\n" +
                   BuildCompressionDetails(records, config);
        }

        private static string BuildCompressionDetails(
            List<DamageFieldRecord> records,
            StatCompressionStatConfig config)
        {
            var parameter = StatCompressionRuntime.GetActualParameter(
                config.method,
                StatCompressionMod.Settings.method,
                StatCompressionMod.Settings.parameter,
                config.tScale);
            var actualMethod = StatCompressionRuntime.ResolveMethod(
                config.method,
                StatCompressionMod.Settings.method);
            var actualThreshold = config.thresholdFactor;
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
                (actualThreshold * 100f).ToString("0.###") + "%"));
            lines.Add(StatCompressionText.T("StatCompression_BaseDamage_Info_AfterFactors"));
            lines.Add("</color>");
            return string.Join("\n", lines);
        }

        private static string FormatDamage(float value)
        {
            return value.ToString("0.###");
        }

        private static void RefreshDerivedStats()
        {
            var stats = DefDatabase<StatDef>.AllDefsListForReading;
            for (var i = 0; i < stats.Count; i++)
            {
                stats[i].Worker.TryClearCache();
            }

            StatsReportUtility.Reset();
        }

        private abstract class DamageFieldRecord
        {
            protected DamageFieldRecord(BaseDamageCategory category, Def owner, string sourceLabel)
            {
                Category = category;
                SourceLabel = sourceLabel;
                AddOwner(owner);
            }

            public BaseDamageCategory Category { get; }
            public string SourceLabel { get; }
            public float Original { get; protected set; }
            public float Applied { get; protected set; }
            public bool Changed => Math.Abs(Original - Applied) > 0.0001f;

            public void AddOwner(Def owner)
            {
                if (owner == null)
                {
                    return;
                }

                if (!RecordsByOwner.TryGetValue(owner, out var records))
                {
                    records = new List<DamageFieldRecord>();
                    RecordsByOwner.Add(owner, records);
                }

                if (!records.Contains(this))
                {
                    records.Add(this);
                }
            }

            public abstract void Restore();
            public abstract void Apply(ref CompiledStatConfig config);

            protected static float Compress(float original, ref CompiledStatConfig config)
            {
                return StatCompressionRuntimeCompiler.ApplyStatic(ref config, original);
            }

            protected static int CompressInt(int original, ref CompiledStatConfig config)
            {
                var compressed = Compress(original, ref config);
                if (original > 0 && compressed > 0f)
                {
                    return Math.Max(1, Mathf.RoundToInt(compressed));
                }

                return Mathf.RoundToInt(compressed);
            }
        }

        private sealed class ToolDamageRecord : DamageFieldRecord
        {
            private readonly Tool target;

            public ToolDamageRecord(Tool target, Def owner) :
                base(BaseDamageCategory.MeleeBase, owner, target.LabelCap)
            {
                this.target = target;
                Original = target.power;
                Applied = Original;
            }

            public override void Restore()
            {
                target.power = Original;
                Applied = Original;
            }

            public override void Apply(ref CompiledStatConfig config)
            {
                Applied = Compress(Original, ref config);
                target.power = Applied;
            }
        }

        private sealed class VerbDamageRecord : DamageFieldRecord
        {
            private readonly VerbProperties target;

            public VerbDamageRecord(VerbProperties target, Def owner) :
                base(BaseDamageCategory.MeleeBase, owner, StatCompressionText.T("StatCompression_BaseDamage_Source_NonTool"))
            {
                this.target = target;
                Original = target.meleeDamageBaseAmount;
                Applied = Original;
            }

            public override void Restore()
            {
                target.meleeDamageBaseAmount = (int)Original;
                Applied = Original;
            }

            public override void Apply(ref CompiledStatConfig config)
            {
                var value = CompressInt((int)Original, ref config);
                Applied = value;
                target.meleeDamageBaseAmount = value;
            }
        }

        private sealed class ExtraDamageRecord : DamageFieldRecord
        {
            private readonly ExtraDamage target;

            public ExtraDamageRecord(
                ExtraDamage target,
                BaseDamageCategory category,
                Def owner,
                string sourceLabel) : base(category, owner, sourceLabel)
            {
                this.target = target;
                Original = target.amount;
                Applied = Original;
            }

            public override void Restore()
            {
                target.amount = Original;
                Applied = Original;
            }

            public override void Apply(ref CompiledStatConfig config)
            {
                Applied = Compress(Original, ref config);
                target.amount = Applied;
            }
        }

        private sealed class ProjectileDamageRecord : DamageFieldRecord
        {
            private readonly ProjectileProperties target;
            private readonly int originalDamage;
            private readonly float originalArmorPenetration;
            private readonly int effectiveOriginal;

            public ProjectileProperties Target => target;

            public ProjectileDamageRecord(
                ProjectileProperties target,
                BaseDamageCategory category,
                Def owner,
                string sourceLabel) : base(category, owner, sourceLabel)
            {
                this.target = target;
                originalDamage = ProjectileDamage(target);
                originalArmorPenetration = ProjectileArmorPenetration(target);
                effectiveOriginal = originalDamage >= 0
                    ? originalDamage
                    : target.damageDef?.defaultDamage ?? -1;
                Original = effectiveOriginal;
                Applied = Original;
            }

            public override void Restore()
            {
                ProjectileDamage(target) = originalDamage;
                ProjectileArmorPenetration(target) = originalArmorPenetration;
                Applied = Original;
            }

            public override void Apply(ref CompiledStatConfig config)
            {
                if (effectiveOriginal < 0)
                {
                    return;
                }

                var value = CompressInt(effectiveOriginal, ref config);
                Applied = value;
                if (value == effectiveOriginal && originalDamage < 0)
                {
                    return;
                }

                ProjectileDamage(target) = value;
                if (originalDamage < 0 && originalArmorPenetration < 0f &&
                    target.damageDef != null && target.damageDef.defaultArmorPenetration >= 0f)
                {
                    ProjectileArmorPenetration(target) = target.damageDef.defaultArmorPenetration;
                }
            }
        }

        private sealed class CompExplosionDamageRecord : DamageFieldRecord
        {
            private readonly CompProperties_Explosive target;
            private readonly int originalDamage;
            private readonly float originalArmorPenetration;
            private readonly int effectiveOriginal;

            public CompExplosionDamageRecord(CompProperties_Explosive target, Def owner) :
                base(BaseDamageCategory.Explosion, owner, target.GetType().Name)
            {
                this.target = target;
                originalDamage = target.damageAmountBase;
                originalArmorPenetration = target.armorPenetrationBase;
                effectiveOriginal = originalDamage >= 0
                    ? originalDamage
                    : target.explosiveDamageType?.defaultDamage ?? -1;
                Original = effectiveOriginal;
                Applied = Original;
            }

            public override void Restore()
            {
                target.damageAmountBase = originalDamage;
                target.armorPenetrationBase = originalArmorPenetration;
                Applied = Original;
            }

            public override void Apply(ref CompiledStatConfig config)
            {
                if (effectiveOriginal < 0)
                {
                    return;
                }

                var value = CompressInt(effectiveOriginal, ref config);
                Applied = value;
                if (value == effectiveOriginal && originalDamage < 0)
                {
                    return;
                }

                target.damageAmountBase = value;
                if (originalDamage < 0 && originalArmorPenetration < 0f &&
                    target.explosiveDamageType != null && target.explosiveDamageType.defaultArmorPenetration >= 0f)
                {
                    target.armorPenetrationBase = target.explosiveDamageType.defaultArmorPenetration;
                }
            }
        }

        private sealed class AbilityExplosionDamageRecord : DamageFieldRecord
        {
            private readonly CompProperties_AbilityExplosion target;
            private readonly int originalDamage;
            private readonly int effectiveOriginal;

            public AbilityExplosionDamageRecord(CompProperties_AbilityExplosion target, Def owner) :
                base(BaseDamageCategory.Explosion, owner, target.GetType().Name)
            {
                this.target = target;
                originalDamage = target.damageAmount;
                effectiveOriginal = originalDamage >= 0 ? originalDamage : target.damageDef?.defaultDamage ?? -1;
                Original = effectiveOriginal;
                Applied = Original;
            }

            public override void Restore()
            {
                target.damageAmount = originalDamage;
                Applied = Original;
            }

            public override void Apply(ref CompiledStatConfig config)
            {
                if (effectiveOriginal < 0)
                {
                    return;
                }

                var value = CompressInt(effectiveOriginal, ref config);
                Applied = value;
                if (value != effectiveOriginal || originalDamage >= 0)
                {
                    target.damageAmount = value;
                }
            }
        }

        private sealed class ExplodeOnDeathDamageRecord : DamageFieldRecord
        {
            private readonly HediffCompProperties_ExplodeOnDeath target;

            public ExplodeOnDeathDamageRecord(HediffCompProperties_ExplodeOnDeath target, Def owner) :
                base(BaseDamageCategory.Explosion, owner, target.GetType().Name)
            {
                this.target = target;
                Original = target.damageAmount;
                Applied = Original;
            }

            public override void Restore()
            {
                target.damageAmount = (int)Original;
                Applied = Original;
            }

            public override void Apply(ref CompiledStatConfig config)
            {
                var value = CompressInt((int)Original, ref config);
                Applied = value;
                target.damageAmount = value;
            }
        }

        private sealed class BeamDamageRecord : DamageFieldRecord
        {
            private readonly VerbProperties target;

            public BeamDamageRecord(VerbProperties target, Def owner) :
                base(BaseDamageCategory.Other, owner, StatCompressionText.T("StatCompression_BaseDamage_Source_Beam"))
            {
                this.target = target;
                Original = target.beamTotalDamage;
                Applied = Original;
            }

            public override void Restore()
            {
                target.beamTotalDamage = Original;
                Applied = Original;
            }

            public override void Apply(ref CompiledStatConfig config)
            {
                Applied = Compress(Original, ref config);
                target.beamTotalDamage = Applied;
            }
        }

        private sealed class IntervalDamageRecord : DamageFieldRecord
        {
            private readonly CompProperties_DamageOnInterval target;

            public IntervalDamageRecord(CompProperties_DamageOnInterval target, Def owner) :
                base(BaseDamageCategory.Other, owner, target.GetType().Name)
            {
                this.target = target;
                Original = target.damage;
                Applied = Original;
            }

            public override void Restore()
            {
                target.damage = Original;
                Applied = Original;
            }

            public override void Apply(ref CompiledStatConfig config)
            {
                Applied = Compress(Original, ref config);
                target.damage = Applied;
            }
        }

        private sealed class Collector
        {
            private readonly Dictionary<Tool, ToolDamageRecord> tools =
                new Dictionary<Tool, ToolDamageRecord>(ReferenceComparer<Tool>.Instance);
            private readonly Dictionary<VerbProperties, VerbDamageRecord> verbs =
                new Dictionary<VerbProperties, VerbDamageRecord>(ReferenceComparer<VerbProperties>.Instance);
            private readonly Dictionary<VerbProperties, BeamDamageRecord> beams =
                new Dictionary<VerbProperties, BeamDamageRecord>(ReferenceComparer<VerbProperties>.Instance);
            private readonly Dictionary<ExtraDamage, ExtraDamageRecord> extras =
                new Dictionary<ExtraDamage, ExtraDamageRecord>(ReferenceComparer<ExtraDamage>.Instance);
            private readonly Dictionary<ProjectileProperties, ProjectileDamageRecord> projectiles =
                new Dictionary<ProjectileProperties, ProjectileDamageRecord>(ReferenceComparer<ProjectileProperties>.Instance);
            private readonly HashSet<object> compRecords =
                new HashSet<object>(ReferenceComparer<object>.Instance);

            public void AddTools(List<Tool> values, Def owner)
            {
                if (values == null)
                {
                    return;
                }

                for (var i = 0; i < values.Count; i++)
                {
                    AddTool(values[i], owner);
                }
            }

            public void AddTool(Tool tool, Def owner)
            {
                if (tool == null)
                {
                    return;
                }

                if (!tools.TryGetValue(tool, out var record))
                {
                    record = Add(new ToolDamageRecord(tool, owner));
                    tools.Add(tool, record);
                }
                else
                {
                    record.AddOwner(owner);
                }

                AddExtras(tool.extraMeleeDamages, BaseDamageCategory.MeleeExtra, owner, tool.LabelCap);
                AddExtras(
                    tool.surpriseAttack?.extraMeleeDamages,
                    BaseDamageCategory.MeleeExtra,
                    owner,
                    tool.LabelCap);
            }

            public void AddVerbs(List<VerbProperties> values, Def owner)
            {
                if (values == null)
                {
                    return;
                }

                for (var i = 0; i < values.Count; i++)
                {
                    AddVerb(values[i], owner);
                }
            }

            public void AddVerb(VerbProperties verb, Def owner)
            {
                if (verb == null)
                {
                    return;
                }

                if (verb.IsMeleeAttack)
                {
                    if (!verbs.TryGetValue(verb, out var record))
                    {
                        record = Add(new VerbDamageRecord(verb, owner));
                        verbs.Add(verb, record);
                    }
                    else
                    {
                        record.AddOwner(owner);
                    }
                }

                if (verb.beamTotalDamage > 0f)
                {
                    if (!beams.TryGetValue(verb, out var beamRecord))
                    {
                        beamRecord = Add(new BeamDamageRecord(verb, owner));
                        beams.Add(verb, beamRecord);
                    }
                    else
                    {
                        beamRecord.AddOwner(owner);
                    }
                }

                AddExtras(
                    verb.surpriseAttack?.extraMeleeDamages,
                    BaseDamageCategory.MeleeExtra,
                    owner,
                    StatCompressionText.T("StatCompression_BaseDamage_Source_Surprise"));
                if (verb.defaultProjectile != null)
                {
                    AddProjectile(verb.defaultProjectile, owner);
                }
            }

            public void AddProjectile(ThingDef projectileDef, Def owner)
            {
                var projectile = projectileDef?.projectile;
                if (projectile == null)
                {
                    return;
                }

                if (!projectiles.TryGetValue(projectile, out var record))
                {
                    var category = projectile.explosionRadius > 0f ||
                                   projectileDef.thingClass != null &&
                                   typeof(Projectile_Explosive).IsAssignableFrom(projectileDef.thingClass)
                        ? BaseDamageCategory.Explosion
                        : BaseDamageCategory.RangedBase;
                    record = Add(new ProjectileDamageRecord(
                        projectile,
                        category,
                        projectileDef,
                        projectileDef.LabelCap));
                    projectiles.Add(projectile, record);
                }

                record.AddOwner(projectileDef);
                record.AddOwner(owner);
                AddExtras(
                    projectile.extraDamages,
                    BaseDamageCategory.RangedExtra,
                    projectileDef,
                    projectileDef.LabelCap);
                if (owner != projectileDef)
                {
                    AddExtras(
                        projectile.extraDamages,
                        BaseDamageCategory.RangedExtra,
                        owner,
                        projectileDef.LabelCap);
                }
            }

            public void AddExtras(
                List<ExtraDamage> values,
                BaseDamageCategory category,
                Def owner,
                string sourceLabel)
            {
                if (values == null)
                {
                    return;
                }

                for (var i = 0; i < values.Count; i++)
                {
                    var extra = values[i];
                    if (extra == null)
                    {
                        continue;
                    }

                    if (!extras.TryGetValue(extra, out var record))
                    {
                        var label = extra.def == null
                            ? sourceLabel
                            : sourceLabel + " - " + extra.def.LabelCap.ToString();
                        record = Add(new ExtraDamageRecord(extra, category, owner, label));
                        extras.Add(extra, record);
                    }
                    else
                    {
                        record.AddOwner(owner);
                    }
                }
            }

            public void AddThingComp(CompProperties comp, ThingDef owner)
            {
                if (comp == null || !compRecords.Add(comp))
                {
                    return;
                }

                if (comp is CompProperties_Explosive explosive)
                {
                    Add(new CompExplosionDamageRecord(explosive, owner));
                }
                else if (comp is CompProperties_DamageOnInterval interval && interval.damage > 0f)
                {
                    Add(new IntervalDamageRecord(interval, owner));
                }
            }

            public void AddAbilityExplosion(CompProperties_AbilityExplosion comp, AbilityDef owner)
            {
                if (comp != null && compRecords.Add(comp))
                {
                    Add(new AbilityExplosionDamageRecord(comp, owner));
                }
            }

            public void AddExplodeOnDeath(HediffCompProperties_ExplodeOnDeath comp, HediffDef owner)
            {
                if (comp != null && comp.damageAmount >= 0 && compRecords.Add(comp))
                {
                    Add(new ExplodeOnDeathDamageRecord(comp, owner));
                }
            }

            private static T Add<T>(T record) where T : DamageFieldRecord
            {
                Records.Add(record);
                return record;
            }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

            public bool Equals(T x, T y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}

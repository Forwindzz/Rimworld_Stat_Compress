using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class BaseDamageDefStore
    {
        private static readonly Dictionary<Def, List<DamageFieldRecord>> recordsByOwner =
            new Dictionary<Def, List<DamageFieldRecord>>(BaseDamageReferenceComparer<Def>.Instance);

        private static readonly List<DamageFieldRecord> records = new List<DamageFieldRecord>();

        public static int RecordCount => records.Count;
        public static int OwnerCount => recordsByOwner.Count;
        public static IEnumerable<KeyValuePair<Def, List<DamageFieldRecord>>> OwnerRecords => recordsByOwner;

        public static int ChangedCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < records.Count; i++)
                {
                    if (records[i].Changed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public static void Scan()
        {
            records.Clear();
            recordsByOwner.Clear();
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
                collector.AddExtras(
                    traitDef.extraDamages,
                    BaseDamageCategory.RangedExtra,
                    traitDef,
                    traitDef.LabelCap);
            }
        }

        public static void Restore()
        {
            for (var i = 0; i < records.Count; i++)
            {
                records[i].Restore();
            }
        }

        public static void Apply(StatCompressionSettings settings)
        {
            if (!settings.enabled)
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

            for (var i = 0; i < records.Count; i++)
            {
                ref var config = ref compiled[(int)records[i].Category];
                records[i].Apply(ref config);
            }

            RefreshDerivedStats();
        }

        public static bool TryGetRecords(Def owner, out List<DamageFieldRecord> ownerRecords)
        {
            return recordsByOwner.TryGetValue(owner, out ownerRecords);
        }

        public static List<ProjectileDamageRecord> ProjectileRecordsFor(ThingDef owner)
        {
            var result = new List<ProjectileDamageRecord>();
            if (owner == null ||
                !recordsByOwner.TryGetValue(owner, out var ownerRecords) ||
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

        internal static void AddOwner(DamageFieldRecord record, Def owner)
        {
            if (owner == null)
            {
                return;
            }

            if (!recordsByOwner.TryGetValue(owner, out var ownerRecords))
            {
                ownerRecords = new List<DamageFieldRecord>();
                recordsByOwner.Add(owner, ownerRecords);
            }

            if (!ownerRecords.Contains(record))
            {
                ownerRecords.Add(record);
            }
        }

        public static void RefreshDerivedStats()
        {
            var stats = DefDatabase<StatDef>.AllDefsListForReading;
            for (var i = 0; i < stats.Count; i++)
            {
                stats[i].Worker.TryClearCache();
            }

            StatsReportUtility.Reset();
        }

        private static T Add<T>(T record) where T : DamageFieldRecord
        {
            records.Add(record);
            return record;
        }

        private sealed class Collector
        {
            private readonly Dictionary<Tool, ToolDamageRecord> tools =
                new Dictionary<Tool, ToolDamageRecord>(BaseDamageReferenceComparer<Tool>.Instance);
            private readonly Dictionary<VerbProperties, VerbDamageRecord> verbs =
                new Dictionary<VerbProperties, VerbDamageRecord>(BaseDamageReferenceComparer<VerbProperties>.Instance);
            private readonly Dictionary<VerbProperties, BeamDamageRecord> beams =
                new Dictionary<VerbProperties, BeamDamageRecord>(BaseDamageReferenceComparer<VerbProperties>.Instance);
            private readonly Dictionary<ExtraDamage, ExtraDamageRecord> extras =
                new Dictionary<ExtraDamage, ExtraDamageRecord>(BaseDamageReferenceComparer<ExtraDamage>.Instance);
            private readonly Dictionary<ProjectileProperties, ProjectileDamageRecord> projectiles =
                new Dictionary<ProjectileProperties, ProjectileDamageRecord>(
                    BaseDamageReferenceComparer<ProjectileProperties>.Instance);
            private readonly HashSet<object> compRecords =
                new HashSet<object>(BaseDamageReferenceComparer<object>.Instance);

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

            private void AddTool(Tool tool, Def owner)
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
        }
    }

    internal sealed class BaseDamageReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly BaseDamageReferenceComparer<T> Instance =
            new BaseDamageReferenceComparer<T>();

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

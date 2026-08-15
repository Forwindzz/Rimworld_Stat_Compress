using System;
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

    internal abstract class DamageFieldRecord
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
            BaseDamageDefStore.AddOwner(this, owner);
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

    internal sealed class ToolDamageRecord : DamageFieldRecord
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

    internal sealed class VerbDamageRecord : DamageFieldRecord
    {
        private readonly VerbProperties target;

        public VerbDamageRecord(VerbProperties target, Def owner) :
            base(
                BaseDamageCategory.MeleeBase,
                owner,
                StatCompressionText.T("StatCompression_BaseDamage_Source_NonTool"))
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

    internal sealed class ExtraDamageRecord : DamageFieldRecord
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

    internal sealed class ProjectileDamageRecord : DamageFieldRecord
    {
        private static readonly AccessTools.FieldRef<ProjectileProperties, int> ProjectileDamage =
            AccessTools.FieldRefAccess<ProjectileProperties, int>("damageAmountBase");

        private static readonly AccessTools.FieldRef<ProjectileProperties, float> ProjectileArmorPenetration =
            AccessTools.FieldRefAccess<ProjectileProperties, float>("armorPenetrationBase");

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

    internal sealed class CompExplosionDamageRecord : DamageFieldRecord
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
                target.explosiveDamageType != null &&
                target.explosiveDamageType.defaultArmorPenetration >= 0f)
            {
                target.armorPenetrationBase = target.explosiveDamageType.defaultArmorPenetration;
            }
        }
    }

    internal sealed class AbilityExplosionDamageRecord : DamageFieldRecord
    {
        private readonly CompProperties_AbilityExplosion target;
        private readonly int originalDamage;
        private readonly int effectiveOriginal;

        public AbilityExplosionDamageRecord(CompProperties_AbilityExplosion target, Def owner) :
            base(BaseDamageCategory.Explosion, owner, target.GetType().Name)
        {
            this.target = target;
            originalDamage = target.damageAmount;
            effectiveOriginal = originalDamage >= 0
                ? originalDamage
                : target.damageDef?.defaultDamage ?? -1;
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

    internal sealed class ExplodeOnDeathDamageRecord : DamageFieldRecord
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

    internal sealed class BeamDamageRecord : DamageFieldRecord
    {
        private readonly VerbProperties target;

        public BeamDamageRecord(VerbProperties target, Def owner) :
            base(
                BaseDamageCategory.Other,
                owner,
                StatCompressionText.T("StatCompression_BaseDamage_Source_Beam"))
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

    internal sealed class IntervalDamageRecord : DamageFieldRecord
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
}

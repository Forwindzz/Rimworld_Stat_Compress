using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace StatCompression
{
    internal static class ObjectTargetFilterRuntime
    {
        internal static CompiledObjectTargetFilter Active =
            ObjectTargetFilterCompiler.MatchAll;

        internal static void Rebuild(ObjectTargetFilterSettings settings)
        {
            Active = ObjectTargetFilterCompiler.Compile(settings);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool MatchesFiltered(
            CompiledObjectTargetFilter plan,
            StatRequest request)
        {
            var thing = request.Thing;
            var pawn = thing as Pawn;
            if (pawn != null && MatchesPawnFiltered(plan, pawn))
            {
                return true;
            }

            var contextPawn = request.Pawn;
            if (contextPawn != null &&
                contextPawn != pawn &&
                MatchesPawnFiltered(plan, contextPawn))
            {
                return true;
            }

            if (thing != null)
            {
                return pawn == null && MatchesThingDef(plan, thing.def);
            }

            var def = request.Def;
            return def != null && MatchesDef(plan, def);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool MatchesPawnFiltered(
            CompiledObjectTargetFilter plan,
            Pawn pawn)
        {
            var mask = plan.pawnMask;
            var faction = pawn.Faction;
            var player = Faction.OfPlayer;
            var isFreeColonist = pawn.IsFreeColonist;

            if ((mask & PawnTargetMask.PlayerColonist) != 0 && isFreeColonist)
            {
                return true;
            }

            if ((mask & PawnTargetMask.PlayerOther) != 0 &&
                !isFreeColonist &&
                (faction == player || pawn.HostFaction == player))
            {
                return true;
            }

            if ((mask & PawnTargetMask.Factionless) != 0 && faction == null)
            {
                return true;
            }

            const PawnTargetMask relationMask =
                PawnTargetMask.Hostile | PawnTargetMask.NonHostile;
            if (faction != null &&
                faction != player &&
                (mask & relationMask) != 0)
            {
                var hostile = faction.HostileTo(player);
                if ((mask & PawnTargetMask.Hostile) != 0 && hostile)
                {
                    return true;
                }

                if ((mask & PawnTargetMask.NonHostile) != 0 && !hostile)
                {
                    return true;
                }
            }

            var raceIndex = pawn.def.index;
            if (plan.raceDefBits[raceIndex])
            {
                return true;
            }

            if (faction != null && plan.factionDefBits[faction.def.index])
            {
                return true;
            }

            return plan.sourceThingDefBits[raceIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchesThingDef(CompiledObjectTargetFilter plan, ThingDef def)
        {
            return plan.sourceThingDefBits[def.index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchesDef(CompiledObjectTargetFilter plan, Def def)
        {
            var thingDef = def as ThingDef;
            if (thingDef != null)
            {
                return MatchesThingDef(plan, thingDef);
            }

            var mod = def.modContentPack;
            return mod != null && plan.sourceModsForOtherDefs.Contains(mod);
        }
    }
}

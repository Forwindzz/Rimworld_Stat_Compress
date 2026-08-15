using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace StatCompression
{
    [Flags]
    internal enum PawnTargetMask : byte
    {
        None = 0,
        PlayerColonist = 1 << 0,
        PlayerOther = 1 << 1,
        Hostile = 1 << 2,
        NonHostile = 1 << 3,
        Factionless = 1 << 4
    }

    internal sealed class CompiledObjectTargetFilter
    {
        internal bool matchAll;
        internal PawnTargetMask pawnMask;
        internal bool[] raceDefBits;
        internal bool[] factionDefBits;
        internal bool[] sourceThingDefBits;
        internal HashSet<ModContentPack> sourceModsForOtherDefs;
    }

    internal static class ObjectTargetFilterCompiler
    {
        internal static readonly CompiledObjectTargetFilter MatchAll =
            new CompiledObjectTargetFilter { matchAll = true };

        internal static CompiledObjectTargetFilter Compile(ObjectTargetFilterSettings settings)
        {
            var thingDefCount = DefDatabase<ThingDef>.AllDefsListForReading.Count;
            var factionDefCount = DefDatabase<FactionDef>.AllDefsListForReading.Count;
            if (!settings.enabled)
            {
                return MatchAll;
            }

            var pawnMask = PawnTargetMask.None;
            if (settings.playerColonists) pawnMask |= PawnTargetMask.PlayerColonist;
            if (settings.playerOtherPawns) pawnMask |= PawnTargetMask.PlayerOther;
            if (settings.hostilePawns) pawnMask |= PawnTargetMask.Hostile;
            if (settings.nonHostilePawns) pawnMask |= PawnTargetMask.NonHostile;
            if (settings.factionlessPawns) pawnMask |= PawnTargetMask.Factionless;

            var sourceMods = ResolveMods(settings.sourceModPackageIds);
            return new CompiledObjectTargetFilter
            {
                matchAll = false,
                pawnMask = pawnMask,
                raceDefBits = BuildThingDefBits(settings.raceDefNames, thingDefCount, "RaceDef"),
                factionDefBits = BuildFactionDefBits(settings.factionDefNames, factionDefCount),
                sourceThingDefBits = BuildSourceThingDefBits(sourceMods, thingDefCount),
                sourceModsForOtherDefs = sourceMods
            };
        }

        private static bool[] BuildThingDefBits(
            List<string> defNames,
            int count,
            string kind)
        {
            var bits = new bool[count];
            var warned = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < defNames.Count; i++)
            {
                var defName = defNames[i];
                if (defName.NullOrEmpty())
                {
                    continue;
                }

                var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    if (warned.Add(defName))
                    {
                        Log.Warning(
                            $"[{StatCompressionConstants.DisplayName}] Object filter {kind} " +
                            $"not found: {defName}. The saved entry was kept but is inactive.");
                    }
                    continue;
                }

                bits[def.index] = true;
            }

            return bits;
        }

        private static bool[] BuildFactionDefBits(List<string> defNames, int count)
        {
            var bits = new bool[count];
            var warned = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < defNames.Count; i++)
            {
                var defName = defNames[i];
                if (defName.NullOrEmpty())
                {
                    continue;
                }

                var def = DefDatabase<FactionDef>.GetNamedSilentFail(defName);
                if (def == null)
                {
                    if (warned.Add(defName))
                    {
                        Log.Warning(
                            $"[{StatCompressionConstants.DisplayName}] Object filter FactionDef " +
                            $"not found: {defName}. The saved entry was kept but is inactive.");
                    }
                    continue;
                }

                bits[def.index] = true;
            }

            return bits;
        }

        private static HashSet<ModContentPack> ResolveMods(List<string> packageIds)
        {
            var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < packageIds.Count; i++)
            {
                if (!packageIds[i].NullOrEmpty())
                {
                    requested.Add(packageIds[i]);
                }
            }

            var result = new HashSet<ModContentPack>();
            var runningMods = LoadedModManager.RunningModsListForReading;
            for (var i = 0; i < runningMods.Count; i++)
            {
                var mod = runningMods[i];
                if (requested.Contains(mod.PackageId) ||
                    requested.Contains(mod.PackageIdPlayerFacing))
                {
                    result.Add(mod);
                    requested.Remove(mod.PackageId);
                    requested.Remove(mod.PackageIdPlayerFacing);
                }
            }

            foreach (var missing in requested)
            {
                Log.Warning(
                    $"[{StatCompressionConstants.DisplayName}] Object filter source Mod " +
                    $"not found: {missing}. The saved entry was kept but is inactive.");
            }

            return result;
        }

        private static bool[] BuildSourceThingDefBits(
            HashSet<ModContentPack> sourceMods,
            int count)
        {
            var bits = new bool[count];
            if (sourceMods.Count == 0)
            {
                return bits;
            }

            var defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (var i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def.modContentPack != null && sourceMods.Contains(def.modContentPack))
                {
                    bits[def.index] = true;
                }
            }

            return bits;
        }
    }
}

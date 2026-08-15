using System.Collections.Generic;
using Verse;

namespace StatCompression
{
    public sealed class ObjectTargetFilterSettings : IExposable
    {
        public bool enabled;
        public bool playerColonists;
        public bool playerOtherPawns;
        public bool hostilePawns;
        public bool nonHostilePawns;
        public bool factionlessPawns;
        public List<string> raceDefNames = new List<string>();
        public List<string> factionDefNames = new List<string>();
        public List<string> sourceModPackageIds = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", false);
            Scribe_Values.Look(ref playerColonists, "playerColonists", false);
            Scribe_Values.Look(ref playerOtherPawns, "playerOtherPawns", false);
            Scribe_Values.Look(ref hostilePawns, "hostilePawns", false);
            Scribe_Values.Look(ref nonHostilePawns, "nonHostilePawns", false);
            Scribe_Values.Look(ref factionlessPawns, "factionlessPawns", false);
            Scribe_Collections.Look(ref raceDefNames, "raceDefNames", LookMode.Value);
            Scribe_Collections.Look(ref factionDefNames, "factionDefNames", LookMode.Value);
            Scribe_Collections.Look(
                ref sourceModPackageIds,
                "sourceModPackageIds",
                LookMode.Value);

            EnsureLists();
        }

        public ObjectTargetFilterSettings Clone()
        {
            var clone = new ObjectTargetFilterSettings();
            clone.CopyFrom(this);
            return clone;
        }

        public void CopyFrom(ObjectTargetFilterSettings source)
        {
            enabled = source.enabled;
            playerColonists = source.playerColonists;
            playerOtherPawns = source.playerOtherPawns;
            hostilePawns = source.hostilePawns;
            nonHostilePawns = source.nonHostilePawns;
            factionlessPawns = source.factionlessPawns;
            raceDefNames = CopyList(source.raceDefNames);
            factionDefNames = CopyList(source.factionDefNames);
            sourceModPackageIds = CopyList(source.sourceModPackageIds);
        }

        public void EnsureLists()
        {
            raceDefNames = raceDefNames ?? new List<string>();
            factionDefNames = factionDefNames ?? new List<string>();
            sourceModPackageIds = sourceModPackageIds ?? new List<string>();
        }

        public int SelectedCount()
        {
            var count = 0;
            if (playerColonists) count++;
            if (playerOtherPawns) count++;
            if (hostilePawns) count++;
            if (nonHostilePawns) count++;
            if (factionlessPawns) count++;
            count += raceDefNames.Count;
            count += factionDefNames.Count;
            count += sourceModPackageIds.Count;
            return count;
        }

        private static List<string> CopyList(List<string> source)
        {
            return source == null ? new List<string>() : new List<string>(source);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed class ObjectTargetFilterWindow : Window
    {
        private const float GroupHeaderHeight = 34f;
        private const float ListHeight = 260f;
        private static readonly Color WarningColor = new Color(1f, 0.68f, 0.35f, 1f);

        private readonly StatCompressionSettings settings;
        private readonly ObjectTargetFilterSettings draft;
        private readonly ObjectTargetFilterOptionList raceList;
        private readonly ObjectTargetFilterOptionList factionList;
        private readonly ObjectTargetFilterOptionList modList;

        private bool pawnExpanded = true;
        private bool raceExpanded;
        private bool factionExpanded;
        private bool modExpanded;
        private bool showHumanlikeRaces = true;
        private bool showMechanoidRaces = true;
        private bool showAnimalRaces = true;
        private bool showOtherRaces = true;
        private Vector2 scrollPosition;

        public override Vector2 InitialSize => new Vector2(1080f, 760f);

        internal ObjectTargetFilterWindow(StatCompressionSettings settings)
        {
            this.settings = settings;
            draft = settings.ObjectTargetFilter.Clone();
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            forcePause = true;

            raceList = new ObjectTargetFilterOptionList(
                BuildRaceOptions(),
                draft.raceDefNames,
                false);
            factionList = new ObjectTargetFilterOptionList(
                BuildFactionOptions(),
                draft.factionDefNames,
                false);
            modList = new ObjectTargetFilterOptionList(
                BuildModOptions(),
                draft.sourceModPackageIds,
                true);
        }

        public override void DoWindowContents(Rect inRect)
        {
            var configChanged = false;
            Text.Font = GameFont.Medium;
            Widgets.Label(
                new Rect(inRect.x, inRect.y, inRect.width, 34f),
                StatCompressionText.T("StatCompression_ObjectFilter_Title"));
            Text.Font = GameFont.Small;

            var y = inRect.y + 40f;
            var enabledRect = new Rect(inRect.x, y, inRect.width, 30f);
            var wasEnabled = draft.enabled;
            Widgets.CheckboxLabeled(
                enabledRect,
                StatCompressionText.T("StatCompression_ObjectFilter_Enable"),
                ref draft.enabled);
            configChanged |= wasEnabled != draft.enabled;
            y += 32f;

            Widgets.Label(
                new Rect(inRect.x, y, inRect.width, 64f),
                StatCompressionText.T("StatCompression_ObjectFilter_Description") + "\n" +
                StatCompressionText.T("StatCompression_ObjectFilter_SpecialScope"));
            y += 66f;

            DrawStatus(new Rect(inRect.x, y, inRect.width, 28f));
            y += 34f;

            var scrollRect = new Rect(
                inRect.x,
                y,
                inRect.width,
                inRect.yMax - y);
            var contentHeight = CalculateContentHeight();
            var view = new Rect(
                0f,
                0f,
                scrollRect.width - 16f,
                Math.Max(scrollRect.height, contentHeight));
            Widgets.BeginScrollView(scrollRect, ref scrollPosition, view);
            configChanged |= DrawGroups(view);
            Widgets.EndScrollView();

            if (configChanged)
            {
                StatCompressionSettingsEditor.ApplyObjectTargetFilter(settings, draft);
                StatCompressionMod.PersistSettings();
            }
        }

        private void DrawStatus(Rect rect)
        {
            var previousColor = GUI.color;
            if (!draft.enabled)
            {
                GUI.color = Color.gray;
                Widgets.Label(
                    rect,
                    StatCompressionText.T("StatCompression_ObjectFilter_StatusDisabled"));
            }
            else
            {
                var selectedCount = draft.SelectedCount();
                if (selectedCount == 0)
                {
                    GUI.color = WarningColor;
                    Widgets.Label(
                        rect,
                        StatCompressionText.T("StatCompression_ObjectFilter_StatusEmpty"));
                }
                else
                {
                    Widgets.Label(
                        rect,
                        StatCompressionText.T(
                            "StatCompression_ObjectFilter_StatusActive",
                            selectedCount));
                }
            }
            GUI.color = previousColor;
        }

        private bool DrawGroups(Rect view)
        {
            var changed = false;
            var y = 0f;
            if (DrawFoldout(
                    new Rect(0f, y, view.width, GroupHeaderHeight),
                    pawnExpanded,
                    StatCompressionText.T("StatCompression_ObjectFilter_GroupPawn"),
                    PawnSelectedCount()))
            {
                pawnExpanded = !pawnExpanded;
            }
            y += GroupHeaderHeight + 4f;
            if (pawnExpanded)
            {
                var pawnRect = new Rect(0f, y, view.width, 196f);
                changed |= DrawPawnOptions(pawnRect);
                y += pawnRect.height + 8f;
            }

            changed |= DrawRaceGroup(view, ref y);
            changed |= DrawListGroup(
                view,
                ref y,
                ref factionExpanded,
                "StatCompression_ObjectFilter_GroupFaction",
                factionList);
            changed |= DrawListGroup(
                view,
                ref y,
                ref modExpanded,
                "StatCompression_ObjectFilter_GroupMod",
                modList);
            return changed;
        }

        private bool DrawRaceGroup(Rect view, ref float y)
        {
            if (DrawFoldout(
                    new Rect(0f, y, view.width, GroupHeaderHeight),
                    raceExpanded,
                    StatCompressionText.T("StatCompression_ObjectFilter_GroupRace"),
                    raceList.SelectedCount))
            {
                raceExpanded = !raceExpanded;
            }
            y += GroupHeaderHeight + 4f;
            if (!raceExpanded)
            {
                return false;
            }

            DrawRaceCategoryFilters(new Rect(4f, y, view.width - 8f, 34f));
            y += 38f;
            raceList.SetCategoryMask(RaceCategoryMask());
            var changed = raceList.Draw(
                new Rect(4f, y, view.width - 8f, ListHeight),
                draft.enabled);
            y += ListHeight + 8f;
            return changed;
        }

        private void DrawRaceCategoryFilters(Rect rect)
        {
            Widgets.Label(
                new Rect(rect.x, rect.y, 72f, rect.height),
                StatCompressionText.T("StatCompression_ObjectFilter_RaceFilter"));
            var optionX = rect.x + 76f;
            var optionWidth = (rect.width - 76f) / 4f;
            Widgets.CheckboxLabeled(
                new Rect(optionX, rect.y, optionWidth, rect.height),
                StatCompressionText.T("StatCompression_ObjectFilter_RaceHumanlike"),
                ref showHumanlikeRaces);
            Widgets.CheckboxLabeled(
                new Rect(optionX + optionWidth, rect.y, optionWidth, rect.height),
                StatCompressionText.T("StatCompression_ObjectFilter_RaceMechanoid"),
                ref showMechanoidRaces);
            Widgets.CheckboxLabeled(
                new Rect(optionX + optionWidth * 2f, rect.y, optionWidth, rect.height),
                StatCompressionText.T("StatCompression_ObjectFilter_RaceAnimal"),
                ref showAnimalRaces);
            Widgets.CheckboxLabeled(
                new Rect(optionX + optionWidth * 3f, rect.y, optionWidth, rect.height),
                StatCompressionText.T("StatCompression_ObjectFilter_RaceOther"),
                ref showOtherRaces);
        }

        private ObjectTargetOptionCategory RaceCategoryMask()
        {
            var mask = ObjectTargetOptionCategory.None;
            if (showHumanlikeRaces) mask |= ObjectTargetOptionCategory.Humanlike;
            if (showMechanoidRaces) mask |= ObjectTargetOptionCategory.Mechanoid;
            if (showAnimalRaces) mask |= ObjectTargetOptionCategory.Animal;
            if (showOtherRaces) mask |= ObjectTargetOptionCategory.Other;
            return mask;
        }

        private bool DrawListGroup(
            Rect view,
            ref float y,
            ref bool expanded,
            string labelKey,
            ObjectTargetFilterOptionList list)
        {
            if (DrawFoldout(
                    new Rect(0f, y, view.width, GroupHeaderHeight),
                    expanded,
                    StatCompressionText.T(labelKey),
                    list.SelectedCount))
            {
                expanded = !expanded;
            }
            y += GroupHeaderHeight + 4f;
            if (!expanded)
            {
                return false;
            }

            var changed = list.Draw(
                new Rect(4f, y, view.width - 8f, ListHeight),
                draft.enabled);
            y += ListHeight + 8f;
            return changed;
        }

        private bool DrawPawnOptions(Rect rect)
        {
            var beforeColonists = draft.playerColonists;
            var beforeOther = draft.playerOtherPawns;
            var beforeHostile = draft.hostilePawns;
            var beforeNonHostile = draft.nonHostilePawns;
            var beforeFactionless = draft.factionlessPawns;
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(8f);
            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && draft.enabled;
            var halfWidth = (inner.width - 4f) / 2f;
            if (Widgets.ButtonText(
                    new Rect(inner.x, inner.y, halfWidth, 30f),
                    StatCompressionText.T("StatCompression_ObjectFilter_SelectAll")))
            {
                SetAllPawnOptions(true);
            }
            if (Widgets.ButtonText(
                    new Rect(inner.x + halfWidth + 4f, inner.y, halfWidth, 30f),
                    StatCompressionText.T("StatCompression_ObjectFilter_ClearAll")))
            {
                SetAllPawnOptions(false);
            }
            var listing = new Listing_Standard();
            listing.Begin(new Rect(inner.x, inner.y + 34f, inner.width, inner.height - 34f));
            listing.CheckboxLabeled(
                StatCompressionText.T("StatCompression_ObjectFilter_PlayerColonists"),
                ref draft.playerColonists);
            listing.CheckboxLabeled(
                StatCompressionText.T("StatCompression_ObjectFilter_PlayerOtherPawns"),
                ref draft.playerOtherPawns);
            listing.CheckboxLabeled(
                StatCompressionText.T("StatCompression_ObjectFilter_HostilePawns"),
                ref draft.hostilePawns);
            listing.CheckboxLabeled(
                StatCompressionText.T("StatCompression_ObjectFilter_NonHostilePawns"),
                ref draft.nonHostilePawns);
            listing.CheckboxLabeled(
                StatCompressionText.T("StatCompression_ObjectFilter_FactionlessPawns"),
                ref draft.factionlessPawns);
            listing.End();
            GUI.enabled = previousEnabled;
            return beforeColonists != draft.playerColonists ||
                   beforeOther != draft.playerOtherPawns ||
                   beforeHostile != draft.hostilePawns ||
                   beforeNonHostile != draft.nonHostilePawns ||
                   beforeFactionless != draft.factionlessPawns;
        }

        private void SetAllPawnOptions(bool value)
        {
            draft.playerColonists = value;
            draft.playerOtherPawns = value;
            draft.hostilePawns = value;
            draft.nonHostilePawns = value;
            draft.factionlessPawns = value;
        }

        private static bool DrawFoldout(
            Rect rect,
            bool expanded,
            string label,
            int selectedCount)
        {
            return StatCompressionUi.DrawFoldoutBar(
                rect,
                expanded,
                label,
                selectedCount.ToString());
        }

        private float CalculateContentHeight()
        {
            var height = GroupHeaderHeight * 4f + 16f;
            if (pawnExpanded) height += 204f;
            if (raceExpanded) height += ListHeight + 46f;
            if (factionExpanded) height += ListHeight + 8f;
            if (modExpanded) height += ListHeight + 8f;
            return height;
        }

        private int PawnSelectedCount()
        {
            var count = 0;
            if (draft.playerColonists) count++;
            if (draft.playerOtherPawns) count++;
            if (draft.hostilePawns) count++;
            if (draft.nonHostilePawns) count++;
            if (draft.factionlessPawns) count++;
            return count;
        }

        private static IEnumerable<ObjectTargetFilterOption> BuildRaceOptions()
        {
            var defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (var i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (def.race == null ||
                    def.thingClass == null ||
                    !typeof(Pawn).IsAssignableFrom(def.thingClass))
                {
                    continue;
                }

                var category = ObjectTargetOptionCategory.Other;
                if (def.race.IsMechanoid)
                {
                    category = ObjectTargetOptionCategory.Mechanoid;
                }
                else if (def.race.Humanlike)
                {
                    category = ObjectTargetOptionCategory.Humanlike;
                }
                else if (def.race.Animal)
                {
                    category = ObjectTargetOptionCategory.Animal;
                }

                var option = CreateDefOption(def);
                option.Category = category;
                yield return option;
            }
        }

        private static IEnumerable<ObjectTargetFilterOption> BuildFactionOptions()
        {
            var defs = DefDatabase<FactionDef>.AllDefsListForReading;
            for (var i = 0; i < defs.Count; i++)
            {
                yield return CreateDefOption(defs[i]);
            }
        }

        private static ObjectTargetFilterOption CreateDefOption(Def def)
        {
            var label = def.LabelCap.ToString();
            if (label.NullOrEmpty())
            {
                label = def.defName;
            }

            var modName = def.modContentPack?.Name ?? "Core";
            var display = label + "  [" + def.defName + "]";
            return new ObjectTargetFilterOption
            {
                Key = def.defName,
                Label = display,
                Detail = modName,
                SearchText = display + " " + modName
            };
        }

        private static IEnumerable<ObjectTargetFilterOption> BuildModOptions()
        {
            var mods = LoadedModManager.RunningModsListForReading;
            for (var i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                var packageId = mod.PackageIdPlayerFacing;
                yield return new ObjectTargetFilterOption
                {
                    Key = packageId,
                    Label = mod.Name + "  [" + packageId + "]",
                    Detail = packageId,
                    SearchText = mod.Name + " " + packageId
                };
            }
        }
    }
}

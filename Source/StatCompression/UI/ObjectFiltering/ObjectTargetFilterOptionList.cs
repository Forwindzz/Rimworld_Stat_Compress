using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace StatCompression
{
    [Flags]
    internal enum ObjectTargetOptionCategory : byte
    {
        None = 0,
        Humanlike = 1 << 0,
        Mechanoid = 1 << 1,
        Animal = 1 << 2,
        Other = 1 << 3,
        All = Humanlike | Mechanoid | Animal | Other
    }

    internal sealed class ObjectTargetFilterOption
    {
        internal string Key;
        internal string Label;
        internal string Detail;
        internal string SearchText;
        internal ObjectTargetOptionCategory Category = ObjectTargetOptionCategory.All;
    }

    internal sealed class ObjectTargetFilterOptionList
    {
        private const float SearchHeight = 30f;
        private const float ActionHeight = 30f;
        private const float RowHeight = 28f;

        private readonly List<ObjectTargetFilterOption> options;
        private readonly List<int> filteredIndices = new List<int>();
        private readonly HashSet<string> selected;
        private readonly List<string> target;
        private readonly StringComparer comparer;
        private string searchText = string.Empty;
        private Vector2 scrollPosition;
        private ObjectTargetOptionCategory categoryMask = ObjectTargetOptionCategory.All;

        internal ObjectTargetFilterOptionList(
            IEnumerable<ObjectTargetFilterOption> source,
            List<string> target,
            bool ignoreCase)
        {
            this.target = target;
            comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            selected = new HashSet<string>(target, comparer);
            options = source
                .GroupBy(option => option.Key, comparer)
                .Select(group => group.First())
                .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.Key, comparer)
                .ToList();

            var known = new HashSet<string>(options.Select(option => option.Key), comparer);
            foreach (var missing in selected)
            {
                if (known.Add(missing))
                {
                    options.Add(new ObjectTargetFilterOption
                    {
                        Key = missing,
                        Label = missing,
                        Detail = StatCompressionText.T("StatCompression_ObjectFilter_MissingEntry"),
                        SearchText = missing,
                        Category = ObjectTargetOptionCategory.All
                    });
                }
            }

            RebuildFilter();
        }

        internal int SelectedCount => selected.Count;

        internal void SetCategoryMask(ObjectTargetOptionCategory value)
        {
            if (categoryMask == value)
            {
                return;
            }

            categoryMask = value;
            RebuildFilter();
            scrollPosition = Vector2.zero;
        }

        internal bool Draw(Rect rect, bool controlsEnabled)
        {
            var changed = false;
            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && controlsEnabled;
            var halfWidth = (rect.width - 4f) / 2f;
            if (Widgets.ButtonText(
                    new Rect(rect.x, rect.y, halfWidth, ActionHeight),
                    StatCompressionText.T("StatCompression_ObjectFilter_SelectAll")))
            {
                changed |= SelectAll();
            }
            if (Widgets.ButtonText(
                    new Rect(rect.x + halfWidth + 4f, rect.y, halfWidth, ActionHeight),
                    StatCompressionText.T("StatCompression_ObjectFilter_ClearAll")))
            {
                changed |= ClearAll();
            }

            var searchLabelWidth = 72f;
            Widgets.Label(
                new Rect(
                    rect.x,
                    rect.y + ActionHeight + 4f,
                    searchLabelWidth,
                    SearchHeight),
                StatCompressionText.T("StatCompression_Search"));
            var nextSearch = Widgets.TextField(
                new Rect(
                    rect.x + searchLabelWidth,
                    rect.y + ActionHeight + 4f,
                    rect.width - searchLabelWidth,
                    SearchHeight),
                searchText);
            if (nextSearch != searchText)
            {
                searchText = nextSearch;
                RebuildFilter();
                scrollPosition = Vector2.zero;
            }

            var listRect = new Rect(
                rect.x,
                rect.y + ActionHeight + SearchHeight + 8f,
                rect.width,
                rect.height - ActionHeight - SearchHeight - 8f);
            Widgets.DrawMenuSection(listRect);
            var inner = listRect.ContractedBy(4f);
            var view = new Rect(
                0f,
                0f,
                inner.width - 16f,
                Math.Max(inner.height, filteredIndices.Count * RowHeight));
            Widgets.BeginScrollView(inner, ref scrollPosition, view);

            var first = Math.Max(0, (int)(scrollPosition.y / RowHeight) - 1);
            var last = Math.Min(
                filteredIndices.Count,
                (int)((scrollPosition.y + inner.height) / RowHeight) + 2);
            for (var rowIndex = first; rowIndex < last; rowIndex++)
            {
                var option = options[filteredIndices[rowIndex]];
                var row = new Rect(0f, rowIndex * RowHeight, view.width, RowHeight);
                if ((rowIndex & 1) != 0)
                {
                    Widgets.DrawLightHighlight(row);
                }
                Widgets.DrawHighlightIfMouseover(row);

                var isSelected = selected.Contains(option.Key);
                var wasSelected = isSelected;
                Widgets.CheckboxLabeled(
                    row.ContractedBy(3f, 1f),
                    option.Label,
                    ref isSelected);
                if (isSelected != wasSelected)
                {
                    if (isSelected)
                    {
                        selected.Add(option.Key);
                    }
                    else
                    {
                        selected.Remove(option.Key);
                    }
                    SyncTarget();
                    changed = true;
                }

                if (!option.Detail.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(row, option.Detail);
                }
            }

            Widgets.EndScrollView();
            GUI.enabled = previousEnabled;
            return changed;
        }

        private void RebuildFilter()
        {
            filteredIndices.Clear();
            var query = searchText?.Trim();
            for (var i = 0; i < options.Count; i++)
            {
                if (query.NullOrEmpty() ||
                    options[i].SearchText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if ((options[i].Category & categoryMask) != 0)
                    {
                        filteredIndices.Add(i);
                    }
                }
            }
        }

        private bool SelectAll()
        {
            var changed = false;
            for (var i = 0; i < options.Count; i++)
            {
                changed |= selected.Add(options[i].Key);
            }
            if (changed)
            {
                SyncTarget();
            }
            return changed;
        }

        private bool ClearAll()
        {
            if (selected.Count == 0)
            {
                return false;
            }

            selected.Clear();
            SyncTarget();
            return true;
        }

        private void SyncTarget()
        {
            target.Clear();
            target.AddRange(selected.OrderBy(value => value, comparer));
        }
    }
}

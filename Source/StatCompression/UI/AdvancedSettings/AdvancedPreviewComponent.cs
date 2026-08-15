using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed partial class AdvancedPreviewComponent
    {
        private const float PreviewRowHeight = 22f;
        private const int GraphSegmentCount = 64;

        private readonly StatCompressionSettings settings;
        private readonly float[] higherPreviewPercents =
            { 50f, 100f, 150f, 200f, 500f, 5000f, 100000f };
        private readonly float[] lowerPreviewPercents =
            { 150f, 100f, 75f, 40f, 10f, 1f, 0.1f };
        private readonly float[] lowerDirectPreviewPercents =
            { -100f, -50f, 0f, 50f, 100f, 200f };
        private readonly string[] higherPreviewBuffers = new string[7];
        private readonly string[] lowerPreviewBuffers = new string[7];
        private readonly string[] lowerDirectPreviewBuffers = new string[6];

        private PreviewKey key;
        private PreviewState state;
        private bool hasState;

        private readonly struct PreviewKey : IEquatable<PreviewKey>
        {
            public PreviewKey(
                StatCompressionStatConfig config,
                GlobalCompressionInput global)
            {
                DefName = config.defName;
                Enabled = config.enabled;
                Method = config.method;
                TScale = config.tScale;
                Baseline = config.baseline;
                ThresholdFactor = config.thresholdFactor;
                Direction = config.direction;
                GlobalMethod = global.Method;
                GlobalParameter = global.Parameter;
                GlobalThreshold = global.ThresholdFactor;
            }

            private string DefName { get; }
            private bool Enabled { get; }
            private CompressionMethod Method { get; }
            private float TScale { get; }
            private float Baseline { get; }
            private float ThresholdFactor { get; }
            private StatCompressionDirection Direction { get; }
            private CompressionMethod GlobalMethod { get; }
            private float GlobalParameter { get; }
            private float GlobalThreshold { get; }

            public bool Equals(PreviewKey other)
            {
                return DefName == other.DefName &&
                       Enabled == other.Enabled &&
                       Method == other.Method &&
                       TScale.Equals(other.TScale) &&
                       Baseline.Equals(other.Baseline) &&
                       ThresholdFactor.Equals(other.ThresholdFactor) &&
                       Direction == other.Direction &&
                       GlobalMethod == other.GlobalMethod &&
                       GlobalParameter.Equals(other.GlobalParameter) &&
                       GlobalThreshold.Equals(other.GlobalThreshold);
            }
        }

        private sealed class PreviewState
        {
            public StatCompressionStatConfig Config;
            public StatDef Stat;
            public string Label;
            public string DetailsText;
            public CompiledStatConfig Compiled;
            public float[] Percents;
            public string[] Buffers;
            public string[] PercentTexts;
            public string[] ActualTexts;
            public float[] MappedPercents;
            public GraphState Graph;
        }

        private sealed class GraphState
        {
            public bool SignedAxis;
            public float MinInput;
            public float MaxInput;
            public float AxisMin;
            public float AxisMax;
            public float YMin;
            public float YMax;
            public float ThresholdPercent;
            public readonly float[] Inputs = new float[GraphSegmentCount + 1];
            public readonly float[] Outputs = new float[GraphSegmentCount + 1];
            public readonly bool[] Valid = new bool[GraphSegmentCount + 1];
        }

        public AdvancedPreviewComponent(StatCompressionSettings settings)
        {
            this.settings = settings;
        }

        public void SetData(
            StatCompressionStatConfig config,
            GlobalCompressionInput global)
        {
            if (config == null)
            {
                Clear();
                return;
            }

            var nextKey = new PreviewKey(config, global);
            if (hasState && nextKey.Equals(key))
            {
                return;
            }

            key = nextKey;
            hasState = true;
            RebuildState(config, global);
        }

        public void Clear()
        {
            hasState = false;
            state = null;
        }

        public void Draw(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(10f);
            if (state == null)
            {
                Widgets.Label(inner, StatCompressionText.T("StatCompression_SelectStatForPreview"));
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 30f), state.Label);
            Text.Font = GameFont.Tiny;
            Widgets.Label(
                new Rect(inner.x, inner.y + 30f, inner.width, 22f),
                state.Config.defName);

            var detailsY = inner.y + 56f;
            var detailsHeight = Mathf.Clamp(
                Text.CalcHeight(state.DetailsText, inner.width),
                220f,
                Mathf.Max(220f, inner.height - 270f));
            Widgets.Label(
                new Rect(inner.x, detailsY, inner.width, detailsHeight),
                state.DetailsText);

            var valuesY = detailsY + detailsHeight + 4f;
            Widgets.DrawLineHorizontal(inner.x, valuesY, inner.width);
            Widgets.Label(
                new Rect(inner.x, valuesY + 4f, inner.width, 22f),
                StatCompressionText.T("StatCompression_AdvancedPreviewValues"));

            var percentColumnWidth = Mathf.Clamp(inner.width * 0.44f, 146f, 190f);
            var columnHeaderY = valuesY + 26f;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(inner.x, columnHeaderY, percentColumnWidth, 18f),
                StatCompressionText.T("StatCompression_AdvancedPreviewPercentColumn"));
            Widgets.Label(
                new Rect(
                    inner.x + percentColumnWidth + 8f,
                    columnHeaderY,
                    inner.width - percentColumnWidth - 8f,
                    18f),
                StatCompressionText.T("StatCompression_AdvancedPreviewActualColumn"));

            var rowY = columnHeaderY + 20f;
            var inputChanged = false;
            for (var i = 0; i < state.Percents.Length; i++)
            {
                inputChanged |= DrawPreviewValueRow(
                    new Rect(
                        inner.x,
                        rowY + i * PreviewRowHeight,
                        inner.width,
                        PreviewRowHeight),
                    percentColumnWidth,
                    i);
            }

            if (inputChanged)
            {
                RebuildDerivedValues();
            }

            var graphTitleY = rowY + state.Percents.Length * PreviewRowHeight + 8f;
            Widgets.DrawLineHorizontal(inner.x, graphTitleY, inner.width);
            Widgets.Label(
                new Rect(inner.x, graphTitleY + 4f, inner.width, 20f),
                StatCompressionText.T("StatCompression_AdvancedPreviewGraph"));
            if (Event.current.type == EventType.Repaint)
            {
                DrawPreviewGraph(
                    new Rect(
                        inner.x,
                        graphTitleY + 26f,
                        inner.width,
                        inner.yMax - graphTitleY - 26f));
            }

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private void RebuildState(
            StatCompressionStatConfig config,
            GlobalCompressionInput global)
        {
            var stat = DefDatabase<StatDef>.GetNamedSilentFail(config.defName);
            var actualParameter = StatCompressionRuntime.GetActualParameter(
                config.method,
                global.Method,
                global.Parameter,
                config.tScale);
            var actualMethod = StatCompressionRuntime.ResolveMethod(
                config.method,
                global.Method);
            var compiled = StatCompressionRuntimeCompiler.CompileConfig(settings, config);
            SelectPreviewValues(config.direction, out var percents, out var buffers);
            for (var i = 0; i < buffers.Length; i++)
            {
                if (buffers[i] == null)
                {
                    buffers[i] = percents[i].ToString("0.###");
                }
            }

            state = new PreviewState
            {
                Config = config,
                Stat = stat,
                Label = LabelFor(config, stat),
                DetailsText = BuildDetails(
                    config,
                    stat,
                    global,
                    ref compiled,
                    actualMethod,
                    actualParameter),
                Compiled = compiled,
                Percents = percents,
                Buffers = buffers,
                PercentTexts = new string[percents.Length],
                ActualTexts = new string[percents.Length],
                MappedPercents = new float[percents.Length],
                Graph = new GraphState()
            };
            RebuildDerivedValues();
        }

        private bool DrawPreviewValueRow(Rect rect, float percentColumnWidth, int index)
        {
            if ((index & 1) != 0)
            {
                Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.025f));
            }

            var oldValue = state.Percents[index];
            var inputRect = new Rect(rect.x, rect.y + 1f, 56f, rect.height - 2f);
            Widgets.TextFieldNumeric(
                inputRect,
                ref state.Percents[index],
                ref state.Buffers[index],
                float.MinValue,
                float.MaxValue);

            var oldWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.LabelFit(
                new Rect(
                    inputRect.xMax + 3f,
                    rect.y,
                    percentColumnWidth - inputRect.width - 3f,
                    rect.height),
                state.PercentTexts[index]);
            Widgets.LabelFit(
                new Rect(
                    rect.x + percentColumnWidth + 8f,
                    rect.y,
                    rect.width - percentColumnWidth - 8f,
                    rect.height),
                state.ActualTexts[index]);
            Text.WordWrap = oldWordWrap;
            TooltipHandler.TipRegion(
                rect,
                state.PercentTexts[index] + "    " + state.ActualTexts[index]);
            return !oldValue.Equals(state.Percents[index]);
        }

        private void RebuildDerivedValues()
        {
            var compiled = state.Compiled;
            for (var i = 0; i < state.Percents.Length; i++)
            {
                var inputPercent = state.Percents[i];
                var original = state.Config.baseline * inputPercent / 100f;
                var final = StatCompressionRuntimeCompiler.ApplyStatic(ref compiled, original);
                var mappedPercent = final / state.Config.baseline * 100f;
                state.MappedPercents[i] = mappedPercent;
                state.PercentTexts[i] = "%  →  " + FormatPreviewPercent(mappedPercent);
                state.ActualTexts[i] = FormatStatValue(state.Stat, original) +
                                       " -> " +
                                       FormatStatValue(state.Stat, final);
            }

            state.Compiled = compiled;
            RebuildGraph();
        }

        private void RebuildGraph()
        {
            var graph = state.Graph;
            var minInput = state.Percents[0];
            var maxInput = state.Percents[0];
            for (var i = 1; i < state.Percents.Length; i++)
            {
                minInput = Math.Min(minInput, state.Percents[i]);
                maxInput = Math.Max(maxInput, state.Percents[i]);
            }

            var signedAxis = minInput <= 0f;
            if (!signedAxis)
            {
                minInput = Math.Max(0.000001f, minInput);
                maxInput = Math.Max(minInput * 1.001f, maxInput);
            }

            var axisMin = TransformInputAxis(minInput, signedAxis);
            var axisMax = TransformInputAxis(maxInput, signedAxis);
            if (Math.Abs(axisMax - axisMin) < 0.000001f)
            {
                axisMin -= 1f;
                axisMax += 1f;
            }

            var minOutput = 0f;
            var maxOutput = 0f;
            var compiled = state.Compiled;
            for (var i = 0; i <= GraphSegmentCount; i++)
            {
                var inputPercent = InverseInputAxis(
                    Mathf.Lerp(axisMin, axisMax, i / (float)GraphSegmentCount),
                    signedAxis);
                var outputPercent = PreviewMappedPercent(
                    state.Config,
                    ref compiled,
                    inputPercent);
                graph.Inputs[i] = inputPercent;
                graph.Outputs[i] = outputPercent;
                graph.Valid[i] = !float.IsNaN(outputPercent) && !float.IsInfinity(outputPercent);
                if (graph.Valid[i])
                {
                    minOutput = Math.Min(minOutput, outputPercent);
                    maxOutput = Math.Max(maxOutput, outputPercent);
                }
            }

            if (Math.Abs(maxOutput - minOutput) < 0.000001f)
            {
                minOutput -= 1f;
                maxOutput += 1f;
            }

            var outputPadding = (maxOutput - minOutput) * 0.06f;
            graph.SignedAxis = signedAxis;
            graph.MinInput = minInput;
            graph.MaxInput = maxInput;
            graph.AxisMin = axisMin;
            graph.AxisMax = axisMax;
            graph.YMin = minOutput - outputPadding;
            graph.YMax = maxOutput + outputPadding;
            graph.ThresholdPercent = state.Compiled.thresholdValue /
                                     state.Config.baseline *
                                     100f;
            state.Compiled = compiled;
        }

    }
}

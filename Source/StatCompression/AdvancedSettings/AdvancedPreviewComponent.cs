using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace StatCompression
{
    internal sealed class AdvancedPreviewComponent
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

        private void DrawPreviewGraph(Rect rect)
        {
            if (rect.height < 90f || rect.width < 220f)
            {
                return;
            }

            var graph = state.Graph;
            var plot = new Rect(rect.x + 46f, rect.y + 20f, rect.width - 56f, rect.height - 46f);
            Widgets.DrawBoxSolid(plot, new Color(0.08f, 0.09f, 0.1f, 0.72f));

            var oldFont = Text.Font;
            var oldAnchor = Text.Anchor;
            var oldWordWrap = Text.WordWrap;
            Text.Font = GameFont.Tiny;
            Text.WordWrap = false;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(plot.x, rect.y, plot.width * 0.5f, 18f),
                StatCompressionText.T("StatCompression_AdvancedPreviewGraphYAxis"));
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(
                new Rect(plot.x + plot.width * 0.5f, rect.y, plot.width * 0.5f, 18f),
                StatCompressionText.T(
                    graph.SignedAxis
                        ? "StatCompression_AdvancedPreviewGraphXAxisSigned"
                        : "StatCompression_AdvancedPreviewGraphXAxis"));

            var gridColor = new Color(0.55f, 0.57f, 0.59f, 0.28f);
            for (var i = 0; i <= 4; i++)
            {
                var fraction = i / 4f;
                var y = Mathf.Lerp(plot.yMax, plot.y, fraction);
                Widgets.DrawLine(
                    new Vector2(plot.x, y),
                    new Vector2(plot.xMax, y),
                    gridColor,
                    1f);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(
                    new Rect(rect.x, y - 9f, 42f, 18f),
                    FormatAxisPercent(Mathf.Lerp(graph.YMin, graph.YMax, fraction)));
            }

            if (graph.SignedAxis)
            {
                for (var i = 0; i <= 4; i++)
                {
                    var fraction = i / 4f;
                    var axisValue = Mathf.Lerp(graph.AxisMin, graph.AxisMax, fraction);
                    DrawInputGridLine(
                        plot,
                        fraction,
                        FormatAxisPercent(InverseInputAxis(axisValue, true)),
                        gridColor);
                }
            }
            else
            {
                var firstDecade = (int)Math.Ceiling(graph.AxisMin);
                var lastDecade = (int)Math.Floor(graph.AxisMax);
                for (var decade = firstDecade; decade <= lastDecade; decade++)
                {
                    DrawInputGridLine(
                        plot,
                        (decade - graph.AxisMin) / (graph.AxisMax - graph.AxisMin),
                        FormatAxisPercent((float)Math.Pow(10d, decade)),
                        gridColor);
                }
            }

            if (graph.ThresholdPercent >= graph.MinInput &&
                graph.ThresholdPercent <= graph.MaxInput)
            {
                var thresholdX = Mathf.Lerp(
                    plot.x,
                    plot.xMax,
                    (TransformInputAxis(graph.ThresholdPercent, graph.SignedAxis) - graph.AxisMin) /
                    (graph.AxisMax - graph.AxisMin));
                Widgets.DrawLine(
                    new Vector2(thresholdX, plot.y),
                    new Vector2(thresholdX, plot.yMax),
                    new Color(0.95f, 0.72f, 0.25f, 0.8f),
                    1.5f);
            }

            var curveColor = new Color(0.35f, 0.78f, 0.92f, 1f);
            var hasPrevious = false;
            var previous = Vector2.zero;
            for (var i = 0; i <= GraphSegmentCount; i++)
            {
                if (!graph.Valid[i])
                {
                    hasPrevious = false;
                    continue;
                }

                var point = new Vector2(
                    Mathf.Lerp(plot.x, plot.xMax, i / (float)GraphSegmentCount),
                    Mathf.Lerp(
                        plot.yMax,
                        plot.y,
                        Mathf.InverseLerp(graph.YMin, graph.YMax, graph.Outputs[i])));
                if (hasPrevious)
                {
                    Widgets.DrawLine(previous, point, curveColor, 2f);
                }

                previous = point;
                hasPrevious = true;
            }

            for (var i = 0; i < state.Percents.Length; i++)
            {
                var inputPercent = state.Percents[i];
                var outputPercent = state.MappedPercents[i];
                if (float.IsNaN(outputPercent) || float.IsInfinity(outputPercent))
                {
                    continue;
                }

                var point = new Vector2(
                    Mathf.Lerp(
                        plot.x,
                        plot.xMax,
                        (TransformInputAxis(inputPercent, graph.SignedAxis) - graph.AxisMin) /
                        (graph.AxisMax - graph.AxisMin)),
                    Mathf.Lerp(
                        plot.yMax,
                        plot.y,
                        Mathf.InverseLerp(graph.YMin, graph.YMax, outputPercent)));
                Widgets.DrawBoxSolid(
                    new Rect(point.x - 2f, point.y - 2f, 4f, 4f),
                    Color.white);
            }

            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            Text.WordWrap = oldWordWrap;
        }

        private void SelectPreviewValues(
            StatCompressionDirection direction,
            out float[] values,
            out string[] buffers)
        {
            if (direction == StatCompressionDirection.HigherIsBetter)
            {
                values = higherPreviewPercents;
                buffers = higherPreviewBuffers;
            }
            else if (direction == StatCompressionDirection.LowerDirect)
            {
                values = lowerDirectPreviewPercents;
                buffers = lowerDirectPreviewBuffers;
            }
            else
            {
                values = lowerPreviewPercents;
                buffers = lowerPreviewBuffers;
            }
        }

        private static void DrawInputGridLine(
            Rect plot,
            float fraction,
            string label,
            Color color)
        {
            var x = Mathf.Lerp(plot.x, plot.xMax, fraction);
            Widgets.DrawLine(
                new Vector2(x, plot.y),
                new Vector2(x, plot.yMax),
                color,
                1f);
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(x - 34f, plot.yMax + 1f, 68f, 18f), label);
        }

        private static float TransformInputAxis(float value, bool signed)
        {
            return signed
                ? Math.Sign(value) * (float)Math.Log10(1f + Math.Abs(value) / 100f)
                : (float)Math.Log10(Math.Max(0.000001f, value));
        }

        private static float InverseInputAxis(float value, bool signed)
        {
            return signed
                ? Math.Sign(value) * 100f * ((float)Math.Pow(10d, Math.Abs(value)) - 1f)
                : (float)Math.Pow(10d, value);
        }

        private static float PreviewMappedPercent(
            StatCompressionStatConfig config,
            ref CompiledStatConfig compiled,
            float inputPercent)
        {
            var original = config.baseline * inputPercent / 100f;
            var mapped = StatCompressionRuntimeCompiler.ApplyStatic(ref compiled, original);
            return mapped / config.baseline * 100f;
        }

        private static string BuildDetails(
            StatCompressionStatConfig config,
            StatDef stat,
            GlobalCompressionInput global,
            ref CompiledStatConfig compiled,
            CompressionMethod actualMethod,
            float actualParameter)
        {
            var baseline = FormatValuePair(stat, config.baseline);
            var trigger = FormatValuePair(stat, compiled.thresholdValue);
            var triggerOperator = config.direction == StatCompressionDirection.LowerIsBetter
                ? "÷"
                : "×";
            var triggerText = StatCompressionText.T(
                "StatCompression_AdvancedDetail_Trigger",
                baseline,
                (config.thresholdFactor * 100f).ToString("0.###") + "%",
                triggerOperator,
                trigger);

            var selectedMethod = config.method == CompressionMethod.FollowGlobal
                ? StatCompressionText.T(
                    "StatCompression_AdvancedDetail_FollowGlobalMethod",
                    StatCompressionText.MethodLabel(actualMethod))
                : StatCompressionText.MethodLabel(actualMethod);
            var methodText = StatCompressionText.T(
                "StatCompression_AdvancedDetail_Method",
                selectedMethod,
                config.tScale.ToString("0.###"),
                actualParameter.ToString("0.###"),
                ParameterMeaning(actualMethod),
                CompressionExpression(actualMethod, actualParameter),
                MethodDescription(actualMethod));

            string directionKey;
            switch (config.direction)
            {
                case StatCompressionDirection.HigherIsBetter:
                    directionKey = "StatCompression_AdvancedDetail_DirectionHigher";
                    break;
                case StatCompressionDirection.LowerDirect:
                    directionKey = "StatCompression_AdvancedDetail_DirectionLowerDirect";
                    break;
                default:
                    directionKey = "StatCompression_AdvancedDetail_DirectionLower";
                    break;
            }

            return triggerText +
                   "\n\n" + methodText +
                   "\n\n" + StatCompressionText.T(directionKey) +
                   "\n\n" + StatCompressionText.T("StatCompression_AdvancedDetail_Flow");
        }

        private static string CompressionExpression(
            CompressionMethod method,
            float parameter)
        {
            var t = parameter.ToString("0.###");
            switch (method)
            {
                case CompressionMethod.Linear:
                    return "F(e) = e × " + t;
                case CompressionMethod.Exponential:
                    return "F(e) = (e + 1)^" + t + " - 1";
                case CompressionMethod.Logarithmic:
                    return "F(e) = ln(1 + ln(" + t + ") × e) ÷ ln(" + t + ")";
                case CompressionMethod.SoftCap:
                    return "F(e) = " + t + " × e ÷ (e + " + t + ")";
                default:
                    return "F(e) = e";
            }
        }

        private static string ParameterMeaning(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_ParameterMeaning_SoftCap");
                default:
                    return string.Empty;
            }
        }

        private static string MethodDescription(CompressionMethod method)
        {
            switch (method)
            {
                case CompressionMethod.Linear:
                    return StatCompressionText.T("StatCompression_MethodDescription_Linear");
                case CompressionMethod.Exponential:
                    return StatCompressionText.T("StatCompression_MethodDescription_Power");
                case CompressionMethod.Logarithmic:
                    return StatCompressionText.T("StatCompression_MethodDescription_Logarithmic");
                case CompressionMethod.SoftCap:
                    return StatCompressionText.T("StatCompression_MethodDescription_SoftCap");
                default:
                    return string.Empty;
            }
        }

        private static string FormatValuePair(StatDef stat, float value)
        {
            var raw = value.ToString("0.###");
            var display = FormatStatValue(stat, value);
            return display == raw ? raw : raw + " (" + display + ")";
        }

        private static string FormatStatValue(StatDef stat, float value)
        {
            return stat == null
                ? value.ToString("0.###")
                : stat.ValueToString(value, stat.toStringNumberSense, true);
        }

        private static string FormatPreviewPercent(float value)
        {
            return value.ToString("0.###") + "%";
        }

        private static string FormatAxisPercent(float value)
        {
            if (value >= 1000000f)
            {
                return value.ToString("0.##E+0") + "%";
            }

            return value.ToString(value < 1f ? "0.###" : "0.##") + "%";
        }

        private static string LabelFor(StatCompressionStatConfig config, StatDef stat)
        {
            return SpecialCompressionConfigs.IsSpecial(config.defName)
                ? SpecialCompressionConfigs.LabelFor(config.defName)
                : stat?.LabelCap.ToString() ?? string.Empty;
        }
    }
}

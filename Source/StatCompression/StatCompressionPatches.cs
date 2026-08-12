using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace StatCompression
{
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.FinalizeValue))]
    internal static class StatWorker_FinalizeValue_Patch
    {
        public static bool BeforePostProcessPatchApplied { get; private set; }

        private static readonly MethodInfo CompressMethod =
            AccessTools.Method(typeof(StatWorker_FinalizeValue_Patch), nameof(CompressBeforePostProcess));

        private static readonly FieldInfo PostProcessCurveField =
            AccessTools.Field(typeof(StatDef), nameof(StatDef.postProcessCurve));

        private static readonly FieldInfo StatField =
            AccessTools.Field(typeof(StatWorker), "stat");

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var curveFieldIndex = codes.FindIndex(instruction => instruction.LoadsField(PostProcessCurveField));
            if (curveFieldIndex < 0)
            {
                BeforePostProcessPatchApplied = false;
                return codes;
            }

            var insertionIndex = -1;
            for (var i = curveFieldIndex - 1; i >= 0; i--)
            {
                if (IsLdarg3(codes[i]))
                {
                    insertionIndex = i;
                    break;
                }
            }

            if (insertionIndex < 0)
            {
                BeforePostProcessPatchApplied = false;
                return codes;
            }

            var injected = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, StatField),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(OpCodes.Call, CompressMethod)
            };

            injected[0].labels.AddRange(codes[insertionIndex].labels);
            codes[insertionIndex].labels.Clear();
            codes.InsertRange(insertionIndex, injected);
            BeforePostProcessPatchApplied = true;
            return codes;
        }

        public static void CompressBeforePostProcess(StatDef stat, ref float value, bool applyPostProcess)
        {
            var settings = StatCompressionMod.Settings;
            if (settings.stage != CompressionStage.BeforePostProcessCurve ||
                !StatCompressionRuntime.CanRun(settings, applyPostProcess))
            {
                return;
            }

            StatCompressionRuntime.Compress(settings, stat, ref value);
        }

        private static bool IsLdarg3(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldarg_3)
            {
                return true;
            }

            if (instruction.opcode == OpCodes.Ldarg_S && instruction.operand is byte byteOperand)
            {
                return byteOperand == 3;
            }

            if (instruction.opcode == OpCodes.Ldarg_S && instruction.operand is short shortOperand)
            {
                return shortOperand == 3;
            }

            if (instruction.opcode == OpCodes.Ldarg_S && instruction.operand is int intOperand)
            {
                return intOperand == 3;
            }

            return instruction.opcode == OpCodes.Ldarg && instruction.operand is int argIndex && argIndex == 3;
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValue), new[] { typeof(StatRequest), typeof(bool) })]
    internal static class StatWorker_GetValue_Patch
    {
        public static void Postfix(StatDef ___stat, StatRequest req, bool applyPostProcess, ref float __result)
        {
            var settings = StatCompressionMod.Settings;
            var shouldRun =
                settings.stage == CompressionStage.GlobalPostfix ||
                (settings.stage == CompressionStage.BeforePostProcessCurve &&
                 settings.autoFallbackToGlobalPostfix &&
                 !StatWorker_FinalizeValue_Patch.BeforePostProcessPatchApplied);

            if (!shouldRun || !StatCompressionRuntime.CanRun(settings, applyPostProcess))
            {
                return;
            }

            StatCompressionRuntime.Compress(settings, ___stat, ref __result);
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetExplanationFinalizePart))]
    internal static class StatWorker_GetExplanationFinalizePart_Patch
    {
        public static void Postfix(
            StatDef ___stat,
            StatRequest req,
            ToStringNumberSense numberSense,
            ref string __result)
        {
            if (StatCompressionRuntime.TryBuildExplanation(
                    StatCompressionMod.Settings,
                    ___stat,
                    req,
                    numberSense,
                    out var explanation))
            {
                __result += "\n" + explanation;
            }
        }
    }
}

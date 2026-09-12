using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using LivingNPCs.Dialogue;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class FarmOutingCollisionPatch
{
    public static void Apply(Harmony harmony)
    {
        // Registered with the behavior system, independently of dialogue engine/coexistence gates.
        var target = AccessTools.Method(typeof(GameLocation), nameof(GameLocation.isCollidingPosition), new[]
        {
            typeof(Rectangle), typeof(xTile.Dimensions.Rectangle), typeof(bool), typeof(int),
            typeof(bool), typeof(Character), typeof(bool), typeof(bool), typeof(bool), typeof(bool)
        });
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(FarmOutingCollisionPatch), nameof(Transpiler)));
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
        var eventActorGetter = AccessTools.PropertyGetter(typeof(Character), nameof(Character.EventActor));
        var matches = code.Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.Calls(eventActorGetter))
            .Select(pair => pair.index)
            .ToArray();
        if (matches.Length != 1)
        {
            DialogueServices.Monitor?.Log(I18n.Get("log.outing.farmBarrierPatchUnavailable"), LogLevel.Warn);
            return code;
        }

        // This getter is used only by the NPCBarrier check in the supported game method.
        // Keep every other collision check, including objects, buildings and terrain, intact.
        CodeInstruction original = code[matches[0]];
        var loadLocation = new CodeInstruction(OpCodes.Ldarg_0);
        loadLocation.labels.AddRange(original.labels);
        loadLocation.blocks.AddRange(original.blocks);
        original.labels.Clear();
        original.blocks.Clear();
        original.opcode = OpCodes.Call;
        original.operand = AccessTools.Method(typeof(FarmOutingCollisionPatch), nameof(IsEventActorOrFarmOuting));
        code.Insert(matches[0], loadLocation);
        return code;
    }

    private static bool IsEventActorOrFarmOuting(Character character, GameLocation location)
    {
        return character.EventActor || FarmOutingPathController.CanCrossNpcBarrier(character, location);
    }
}

// Copyright (c) 2024 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Generic;
using System.Reflection.Emit;

using HarmonyLib;

using PhantomBrigade;

namespace EchKode.PBMods.ZoomOverTimeline
{
	[HarmonyPatch]
	public static class Patch
	{
		[HarmonyPatch(typeof(GameCameraSystem), "UpdateCameraZoom")]
		[HarmonyPrefix]
		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by the Harmony patch system")]
		static void Gcs_UpdateCameraZoomPrefix()
		{
			IsCombatState = IDUtility.IsGameState(GameStates.combat);
		}

		[HarmonyPatch(typeof(GameCameraSystem), "UpdateCameraZoom")]
		[HarmonyTranspiler]
		[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Used by the Harmony patch system")]
		static IEnumerable<CodeInstruction> Gcs_UpdateCameraZoomTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			// Add some additional checks around getting zoom input.
			// Get the zoom input even if the UI is obstructing the mouse cursor (sensor) but only in combat
			// and if the input controller isn't a gamepad.
			// Effectively this C# code:
			// if (!Contexts.sharedInstance.game.isUIObstructingSensor || IDUtility.IsGameState(GameStates.combat) && !InputHelper.gamepad)

			var cm = new CodeMatcher(instructions, generator);
			var getIsUIObstructingSensorMethodInfo = AccessTools.DeclaredPropertyGetter(typeof(GameContext), nameof(GameContext.isUIObstructingSensor));
			var getIsUIObstructingSensorMatch = new CodeMatch(OpCodes.Callvirt, getIsUIObstructingSensorMethodInfo);
			var isCombatState = CodeInstruction.LoadField(typeof(Patch), nameof(IsCombatState));
			var isGamepad = CodeInstruction.LoadField(typeof(InputHelper), nameof(InputHelper.gamepad));

			cm.MatchEndForward(getIsUIObstructingSensorMatch)
				.Advance(1);
			var bypassInput = new CodeMatch(OpCodes.Brfalse_S, cm.Operand);

			cm.Advance(1);
			cm.CreateLabel(out var getInputLabel);
			var jumpToInput = new CodeInstruction(OpCodes.Brfalse_S, getInputLabel);

			cm.Advance(-1)
				.InsertAndAdvance(jumpToInput)
				.InsertAndAdvance(isCombatState)
				.InsertAndAdvance(bypassInput)
				.InsertAndAdvance(isGamepad);

			return cm.InstructionEnumeration();
		}

		public static bool IsCombatState = false;
	}
}

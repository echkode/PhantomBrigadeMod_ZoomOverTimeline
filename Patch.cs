// Copyright (c) 2024 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Generic;
using System.Reflection.Emit;

using HarmonyLib;

using UnityEngine;

namespace EchKode.PBMods.ZoomOverTimeline
{
	[HarmonyPatch]
	public static class Patch
	{
		[HarmonyPatch(typeof(CIViewCombatEventLog), nameof(CIViewCombatEventLog.TryEntry))]
		[HarmonyPostfix]
		static void Civcel_TryEntryPostfix(CIViewCombatEventLog __instance)
		{
			readOverride = !CIViewCombatEventLog.logDisplayAllowed;
			checkHover = CIViewCombatEventLog.logDisplayAllowed;
			zoomBlockers.Clear();

			if (!checkHover)
			{
				if (log)
				{
					Debug.LogFormat(
						"Mod {0} ({1}) combat event log turned off",
						ModLink.modIndex,
						ModLink.modID);
				}
				return;
			}

			var backgroundSprite = __instance.spriteBackground;
			if (!backgroundColliderAttached)
			{
				var collider = backgroundSprite.gameObject.AddComponent<BoxCollider>();
				var w = backgroundSprite.width;
				var h = backgroundSprite.height;
				collider.center = new Vector3(w / 2f, -h / 2f, 0f);
				collider.size = new Vector2(w, h);
				backgroundColliderAttached = true;
			}
			zoomBlockers.Add(backgroundSprite.gameObject);

			zoomBlockers.Add(__instance.scrollBarWidgetRoot.gameObject);
			var found = false;
			foreach (var child in __instance.scrollPanel.widgets)
			{
				if (child is UISprite sprite && sprite.gameObject.name == "Sprite_Draggable")
				{
					zoomBlockers.Add(sprite.gameObject);
					found = true;
					break;
				}
			}

			if (!found)
			{
				if (log)
				{
					Debug.LogWarningFormat(
						"Mod {0} ({1}) unable to find expected widget in scroll panel",
						ModLink.modIndex,
						ModLink.modID);
				}

				Civcel_TryExitPostfix();
				return;
			}

			zoomBlockers.Add(__instance.scrollBarWidgetRoot.gameObject);
			zoomBlockers.Add(__instance.buttonToggleSize.gameObject);
			zoomBlockers.Add(__instance.buttonFilterComms.gameObject);
			zoomBlockers.Add(__instance.buttonFilterEventsFriendly.gameObject);
			zoomBlockers.Add(__instance.buttonFilterEventsEnemy.gameObject);
			zoomBlockers.Add(__instance.buttonFilterOther.gameObject);
		}

		[HarmonyPatch(typeof(CIViewCombatEventLog), nameof(CIViewCombatEventLog.TryExit))]
		[HarmonyPostfix]
		static void Civcel_TryExitPostfix()
		{
			readOverride = false;
			checkHover = false;
			zoomBlockers.Clear();
		}

		[HarmonyPatch(typeof(CIViewCombatEventLog), "ResetSettings")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Civcel_ResetSettingsTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			// Adjust collider when height of background sprite is changed.

			var cm = new CodeMatcher(instructions, generator);
			var spriteFieldInfo = AccessTools.DeclaredField(typeof(CIViewCombatEventLog), nameof(CIViewCombatEventLog.spriteBackground));
			var spriteMatch = new CodeMatch(OpCodes.Ldfld, spriteFieldInfo);
			var adjustCollider = CodeInstruction.Call(typeof(Patch), nameof(AdjustCollider));

			cm.MatchEndForward(spriteMatch)
				.Advance(3)
				.InsertAndAdvance(adjustCollider);

			return cm.InstructionEnumeration();
		}

		[HarmonyPatch(typeof(CIViewCombatEventLog), "UpdateActive")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Civcel_UpdateActiveTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			// Adjust collider when height of background sprite is changed.

			var cm = new CodeMatcher(instructions, generator);
			var spriteFieldInfo = AccessTools.DeclaredField(typeof(CIViewCombatEventLog), nameof(CIViewCombatEventLog.spriteBackground));
			var spriteMatch = new CodeMatch(OpCodes.Ldfld, spriteFieldInfo);
			var adjustCollider = CodeInstruction.Call(typeof(Patch), nameof(AdjustCollider));

			cm.MatchEndForward(spriteMatch)
				.Advance(1)
				.MatchEndForward(spriteMatch)
				.Advance(3)
				.InsertAndAdvance(adjustCollider);

			return cm.InstructionEnumeration();
		}

		[HarmonyPatch(typeof(GameCameraSystem), "UpdateCameraZoom")]
		[HarmonyPrefix]
		static void Gcs_UpdateCameraZoomPrefix()
		{
			ReadZoomInput = !Contexts.sharedInstance.game.isUIObstructingSensor;
			if (ReadZoomInput)
			{
				return;
			}

			if (!checkHover)
			{
				return;
			}

			if (readOverride)
			{
				ReadZoomInput = true;
				return;
			}

			// Showing combat event log.
			var hovered = UICamera.hoveredObject;
			ReadZoomInput = hovered == null;
			if (ReadZoomInput)
			{
				// !!! Should be hovered over some part of the UI.
				return;
			}

			foreach (var blocker in zoomBlockers)
			{
				if (hovered == blocker)
				{
					return;
				}
			}

			ReadZoomInput = true;
		}

		[HarmonyPatch(typeof(GameCameraSystem), "UpdateCameraZoom")]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> Gcs_UpdateCameraZoomTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			// Replace check on isUIObstructingSensor with a read of the boolean in this class.

			var cm = new CodeMatcher(instructions, generator);
			var getIsUIObstructingSensorMethodInfo = AccessTools.DeclaredPropertyGetter(typeof(GameContext), nameof(GameContext.isUIObstructingSensor));
			var getIsUIObstructingSensorMatch = new CodeMatch(OpCodes.Callvirt, getIsUIObstructingSensorMethodInfo);
			var readZoomInput = CodeInstruction.LoadField(typeof(Patch), nameof(ReadZoomInput));

			cm.MatchStartForward(getIsUIObstructingSensorMatch)
				.Advance(-2)
				.RemoveInstructions(3)
				.InsertAndAdvance(readZoomInput)
				.SetOpcodeAndAdvance(OpCodes.Brfalse_S);

			return cm.InstructionEnumeration();
		}

		public static void AdjustCollider()
		{
			if (!backgroundColliderAttached)
			{
				return;
			}

			var sprite = CIViewCombatEventLog.ins.spriteBackground;
			var collider = sprite.gameObject.GetComponent<BoxCollider>();
			var w = sprite.width;
			var h = sprite.height;
			collider.center = new Vector3(w / 2f, -h / 2f, 0f);
			collider.size = new Vector2(w, h);
		}

		public static bool ReadZoomInput = false;

		static bool backgroundColliderAttached = false;
		static bool readOverride = false;
		static bool checkHover = false;
		static readonly List<GameObject> zoomBlockers = new List<GameObject>();

		static bool log = false;
	}
}

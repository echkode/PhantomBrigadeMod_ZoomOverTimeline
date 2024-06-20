// Copyright (c) 2024 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text;

using HarmonyLib;

using UnityEngine;

namespace EchKode.PBMods.ZoomOverTimeline
{
	[HarmonyPatch]
	public static class Patch
	{
		[HarmonyPatch(typeof(CIViewCombatEventLog), "UpdateActive")]
		[HarmonyPostfix]
		static void Civcel_UpdateActivePostfix(CIViewCombatEventLog __instance)
		{
			readOverride = !CIViewCombatEventLog.logDisplayAllowed;
			checkHover = CIViewCombatEventLog.logDisplayAllowed;

			if (!checkHover)
			{
				if (log)
				{
					Debug.LogFormat(
						"Mod {0} ({1}) combat event log turned off",
						ModLink.ModIndex,
						ModLink.ModID);
				}
				zoomBlockers.Clear();
				return;
			}

			if (zoomBlockers.Count == expectedZoomBlockerCount)
			{
				return;
			}

			var backgroundSprite = __instance.spriteBackground;
			if (!backgroundColliderAttached)
			{
				var collider = backgroundSprite.gameObject.AddComponent<BoxCollider>();
				NGUITools.UpdateWidgetCollider(backgroundSprite);
				var t = new Traverse(__instance);
				t.Field<List<Collider>>("colliders").Value.Add(collider);
				backgroundColliderAttached = true;
				if (log)
				{
					var (colliderLowerLeft, colliderUpperRight, colliderCenter) = GetColliderUIPos(backgroundSprite);
					var (lowerLeft, upperRight, center) = GetUIPos(backgroundSprite);
					Debug.LogFormat(
						"Mod {0} ({1}) attached collider | name: {2} | pivot: {3}\n  collider | size: {4} | uipos: {5}x{6}+{7}\n  widget | size: {8} | uipos: {9}x{10}+{11}",
						ModLink.ModIndex,
						ModLink.ModID,
						backgroundSprite.name,
						backgroundSprite.pivot,
						collider.size,
						colliderLowerLeft,
						colliderUpperRight,
						colliderCenter,
						new Vector2Int(backgroundSprite.width, backgroundSprite.height),
						lowerLeft,
						upperRight,
						center);
				}
			}

			zoomBlockers.Clear();
			zoomBlockers.Add(backgroundSprite.gameObject);

			var found = false;
			var scrollPanelWidgets = __instance.scrollPanel.widgets;
			for (var i = 0; i < scrollPanelWidgets.Count; i += 1)
			{
				var child = scrollPanelWidgets[i];
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
					if (scrollPanelWidgets.Count != 0)
					{
						sb.Clear();
						for (var i = 0; i < scrollPanelWidgets.Count; i += 1)
						{
							sb.AppendFormat("\n  {0}", scrollPanelWidgets[i].name);
						}
						Debug.LogWarningFormat(
							"Mod {0} ({1}) unable to find expected widget in scroll panel | widget count: {2}{3}",
							ModLink.ModIndex,
							ModLink.ModID,
							scrollPanelWidgets.Count,
							sb);
					}
					else
					{
						sb.Clear();
						var st = new System.Diagnostics.StackTrace();
						for (var i = 2; i < st.FrameCount; i += 1)
						{
							var frame = st.GetFrame(i);
							var m = frame.GetMethod();
							sb.Append("\n")
								.Append(' ', (i - 1) * 2)
								.AppendFormat("{0}::{1}", m.DeclaringType.FullName, m.Name);
						}
						Debug.LogWarningFormat(
							"Mod {0} ({1}) unable to find expected widget in scroll panel -- no widgets in panel{2}",
							ModLink.ModIndex,
							ModLink.ModID,
							sb);
					}
				}

				readOverride = false;
				checkHover = false;
				zoomBlockers.Clear();
				return;
			}

			zoomBlockers.Add(__instance.scrollBarWidgetRoot.gameObject);
			zoomBlockers.Add(__instance.buttonToggleSize.gameObject);
			zoomBlockers.Add(__instance.buttonFilterComms.gameObject);
			zoomBlockers.Add(__instance.buttonFilterEventsFriendly.gameObject);
			zoomBlockers.Add(__instance.buttonFilterEventsEnemy.gameObject);
			zoomBlockers.Add(__instance.buttonFilterOther.gameObject);

			if (log)
			{
				sb.Clear();
				foreach (var blocker in zoomBlockers)
				{
					sb.AppendFormat("\n  {0}", blocker.name);
				}
				Debug.LogFormat(
					"Mod {0} ({1}) zoom blockers{2}",
					ModLink.ModIndex,
					ModLink.ModID,
					sb);
			}
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

			if (!checkHover || readOverride)
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
					if (log)
					{
						Debug.LogFormat(
							"Mod {0} ({1}) zoom blocked | name: {2}",
							ModLink.ModIndex,
							ModLink.ModID,
							blocker.name);
					}
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

			if (log)
			{
				var (lowerLeft, upperRight, center) = GetColliderUIPos(sprite);
				Debug.LogFormat(
					"Mod {0} ({1}) resized collider | name: {2} | size: {3} | uipos: {4}x{5}+{6}",
					ModLink.ModIndex,
					ModLink.ModID,
					sprite.name,
					collider.size,
					lowerLeft,
					upperRight,
					center);
			}
		}


		static (Vector2Int, Vector2Int, Vector2Int) GetUIPos(UIWidget widget)
		{
			var root = widget.root;
			var corners = widget.worldCorners;
			var lowerLeft = root.transform.InverseTransformPoint(corners[0]);
			var upperRight = root.transform.InverseTransformPoint(corners[2]);
			var center = root.transform.InverseTransformPoint(widget.transform.position);
			return (
				Vector2Int.RoundToInt(lowerLeft),
				Vector2Int.RoundToInt(upperRight),
				Vector2Int.RoundToInt(center));
		}

		static (Vector2Int, Vector2Int, Vector2Int) GetColliderUIPos(UIWidget widget)
		{
			var collider = widget.gameObject.GetComponent<BoxCollider>();
			var halfExtents = new Vector3(collider.size.x / 2, collider.size.y / 2, 0f);
			var lowerLeftLocal = collider.center - halfExtents;
			var upperRightLocal = collider.center + halfExtents;
			var root = widget.root;
			var lowerLeft = collider.transform.TransformPoint(lowerLeftLocal);
			var upperRight = collider.transform.TransformPoint(upperRightLocal);
			lowerLeft = root.transform.InverseTransformPoint(lowerLeft);
			upperRight = root.transform.InverseTransformPoint(upperRight);
			var center = collider.transform.TransformPoint(collider.center);
			center = root.transform.InverseTransformPoint(center);
			return (
				Vector2Int.RoundToInt(lowerLeft),
				Vector2Int.RoundToInt(upperRight),
				Vector2Int.RoundToInt(center));
		}

		public static bool ReadZoomInput = false;

		static bool backgroundColliderAttached = false;
		static bool readOverride = false;
		static bool checkHover = false;
		static readonly List<GameObject> zoomBlockers = new List<GameObject>();
		const int expectedZoomBlockerCount = 8;

		static bool log = false;
		static readonly StringBuilder sb = new StringBuilder();
	}
}

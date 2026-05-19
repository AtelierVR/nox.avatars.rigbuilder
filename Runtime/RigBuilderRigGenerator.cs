using Nox.CCK.Utils;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Logger = Nox.CCK.Utils.Logger;
using RB = UnityEngine.Animations.Rigging.RigBuilder;

namespace Nox.Avatars.RigBuilder {
	/// <summary>
	/// Générateur statique pour les systèmes IK de rigging avatar (Legacy - RigBuilder)
	/// 
	/// Note: GameObject names use "IKRig_" prefix to avoid conflicts with Unity's human bone mapping system.
	/// This prevents ambiguous bone references that can cause avatar validation errors.
	/// 
	/// Ce système est utilisé quand HAS_FINALIK n'est pas défini. 
	/// Quand FinalIK est disponible, préférez utiliser FinalIKRigGenerator.
	/// </summary>
	public static class RigBuilderRigGenerator {
		private static readonly HumanBodyBones[] CriticalBones = {
			HumanBodyBones.Chest,
			HumanBodyBones.Neck,
			HumanBodyBones.Head,
			HumanBodyBones.LeftUpperArm,
			HumanBodyBones.LeftLowerArm,
			HumanBodyBones.LeftHand,
			HumanBodyBones.RightUpperArm,
			HumanBodyBones.RightLowerArm,
			HumanBodyBones.RightHand,
			HumanBodyBones.LeftUpperLeg,
			HumanBodyBones.LeftLowerLeg,
			HumanBodyBones.LeftFoot,
			HumanBodyBones.RightUpperLeg,
			HumanBodyBones.RightLowerLeg,
			HumanBodyBones.RightFoot,
		};

		private static bool ValidateHumanoidBones(RigBuilderAvatarModule module) {
			var animator = module.Descriptor.Animator;
			var root     = module.Descriptor.Anchor;

			// Build a set of all transform names in the full hierarchy to detect duplicates.
			var allTransforms = root.GetComponentsInChildren<Transform>(true);
			var nameCounts    = new System.Collections.Generic.Dictionary<string, int>(allTransforms.Length);
			foreach (var t in allTransforms) {
				var n = t.name;
				nameCounts[n] = nameCounts.TryGetValue(n, out var c) ? c + 1 : 1;
			}

			foreach (var bone in CriticalBones) {
				var boneTransform = module.GetBone(bone);
				if (!boneTransform) {
					Logger.LogError(
						$"Cannot build IK rig: humanoid bone '{bone}' could not be resolved. " +
						"The avatar may have duplicate transform names in its hierarchy (e.g. two bones " +
						"with the same name under different parents). Fix the avatar to avoid " +
						"TransformStreamHandle crashes in the animation rigging system.",
						context: root,
						tag: nameof(RigBuilderRigGenerator)
					);
					return false;
				}

				// Detect duplicate names: if another transform shares the bone's name,
				// GetBoneTransform may have resolved to the wrong one (not in the humanoid skeleton).
				// That wrong transform is not in the AnimationStream → TransformStreamHandle crash.
				if (nameCounts.TryGetValue(boneTransform.name, out var count) && count > 1) {
					Logger.LogError(
						$"Cannot build IK rig: transform name '{boneTransform.name}' (used for humanoid bone '{bone}') " +
						$"appears {count} times in the avatar hierarchy. All bone names must be unique. " +
						"Rename the conflicting transforms (e.g. inside accessory objects like 'RindoHand') " +
						"to avoid TransformStreamHandle crashes.",
						context: root,
						tag: nameof(RigBuilderRigGenerator)
					);
					return false;
				}
			}
			return true;
		}

		public static RB Create(RigBuilderAvatarModule module, IRuntimeAvatar runtime) {
			var rigBuilder = CreateRigBuilder(module);
			rigBuilder.enabled = false;

			if (!ValidateHumanoidBones(module))
				return rigBuilder; // leave disabled — avoids Burst TransformStreamHandle crash

			CreateUpperSpine(module, rigBuilder);
			CreateLeftArm(module, rigBuilder);
			CreateRightArm(module, rigBuilder);
			CreateLeftLeg(module, rigBuilder);
			CreateRightLeg(module, rigBuilder);
			CreateLeftToe(module, rigBuilder);
			CreateRightToe(module, rigBuilder);

			rigBuilder.enabled = true;
			return rigBuilder;
		}

		private static RB CreateRigBuilder(RigBuilderAvatarModule module) {
			var rigBuilder = module.GetRig()
				?? module.Descriptor
					.Anchor
					.GetOrAddComponent<UnityEngine.Animations.Rigging.RigBuilder>();
			rigBuilder.layers.Clear();
			return rigBuilder;
		}

		private static void CreateUpperSpine(RigBuilderAvatarModule module, RB rigBuilder) {
			var upperSpine = new GameObject(GetRigFromBone(HumanBodyBones.Head));
			upperSpine.transform.SetParent(rigBuilder.transform);
			upperSpine.transform.localPosition = Vector3.zero;
			upperSpine.transform.localRotation = Quaternion.identity;
			upperSpine.transform.localScale    = Vector3.one;

			var rig = upperSpine.AddComponent<Rig>();
			rig.weight = 1.0f;
			rigBuilder.layers.Add(new RigLayer(rig));

			var contraint = new GameObject("UpperSpineConstraint");
			contraint.transform.SetParent(upperSpine.transform);
			contraint.transform.localPosition = Vector3.zero;
			contraint.transform.localRotation = Quaternion.identity;
			contraint.transform.localScale    = Vector3.one;
			var constraint = contraint.AddComponent<TwoBoneIKConstraint>();

			constraint.data.root   = module.GetBone(HumanBodyBones.Chest);
			constraint.data.mid    = module.GetBone(HumanBodyBones.Neck);
			constraint.data.tip    = module.GetBone(HumanBodyBones.Head);
			constraint.data.target = module.GetOrAddPart(HumanBodyBones.Head, upperSpine.transform);
			constraint.data.hint   = module.GetOrAddPart(HumanBodyBones.Neck, upperSpine.transform);

			if (constraint.data.root)
				constraint.data.root.gameObject.GetOrAddComponent<RigTransform>();
			if (constraint.data.mid)
				constraint.data.mid.gameObject.GetOrAddComponent<RigTransform>();
			if (constraint.data.tip)
				constraint.data.tip.gameObject.GetOrAddComponent<RigTransform>();

			constraint.data.targetPositionWeight = 1.0f;
			constraint.data.targetRotationWeight = 1.0f;
			constraint.data.hintWeight           = 1.0f;
		}

		private static void CreateLeftArm(RigBuilderAvatarModule module, RB rigBuilder)
			=> CreateArm(
				GetRigFromBone(HumanBodyBones.LeftUpperArm),
				HumanBodyBones.LeftUpperArm,
				HumanBodyBones.LeftLowerArm,
				HumanBodyBones.LeftHand,
				module, rigBuilder
			);

		private static void CreateRightArm(RigBuilderAvatarModule module, RB rigBuilder)
			=> CreateArm(
				GetRigFromBone(HumanBodyBones.RightUpperArm),
				HumanBodyBones.RightUpperArm,
				HumanBodyBones.RightLowerArm,
				HumanBodyBones.RightHand,
				module, rigBuilder
			);

		private static void CreateArm(string name, HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones handBone, RigBuilderAvatarModule module, RB rigBuilder) {
			var arm = new GameObject(name);
			arm.transform.SetParent(rigBuilder.transform);
			arm.transform.localPosition = Vector3.zero;
			arm.transform.localRotation = Quaternion.identity;
			arm.transform.localScale    = Vector3.one;

			var rig = arm.AddComponent<Rig>();
			rig.weight = 1.0f;
			rigBuilder.layers.Add(new RigLayer(rig));

			var contraint = new GameObject($"{name}Constraint");
			contraint.transform.SetParent(arm.transform);
			contraint.transform.localPosition = Vector3.zero;
			contraint.transform.localRotation = Quaternion.identity;
			contraint.transform.localScale    = Vector3.one;
			var constraint = contraint.AddComponent<TwoBoneIKConstraint>();

			constraint.data.root   = module.GetBone(upperBone);
			constraint.data.mid    = module.GetBone(lowerBone);
			constraint.data.tip    = module.GetBone(handBone);
			constraint.data.target = module.GetOrAddPart(handBone, arm.transform);
			constraint.data.hint   = module.GetOrAddPart(lowerBone, arm.transform);

			if (constraint.data.root)
				constraint.data.root.gameObject.GetOrAddComponent<RigTransform>();
			if (constraint.data.mid)
				constraint.data.mid.gameObject.GetOrAddComponent<RigTransform>();
			if (constraint.data.tip)
				constraint.data.tip.gameObject.GetOrAddComponent<RigTransform>();

			constraint.data.targetPositionWeight = 1.0f;
			constraint.data.targetRotationWeight = 1.0f;
			constraint.data.hintWeight           = 1.0f;
		}

		private static void CreateLeftLeg(RigBuilderAvatarModule module, RB rigBuilder)
			=> CreateLeg(
				GetRigFromBone(HumanBodyBones.LeftUpperLeg),
				HumanBodyBones.LeftUpperLeg,
				HumanBodyBones.LeftLowerLeg,
				HumanBodyBones.LeftFoot,
				module, rigBuilder
			);

		private static void CreateRightLeg(RigBuilderAvatarModule module, RB rigBuilder)
			=> CreateLeg(
				GetRigFromBone(HumanBodyBones.RightUpperLeg),
				HumanBodyBones.RightUpperLeg,
				HumanBodyBones.RightLowerLeg,
				HumanBodyBones.RightFoot,
				module, rigBuilder
			);

		private static void CreateLeg(string name, HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones footBone, RigBuilderAvatarModule module, RB rigBuilder) {
			var leg = new GameObject(name);
			leg.transform.SetParent(rigBuilder.transform);
			leg.transform.localPosition = Vector3.zero;
			leg.transform.localRotation = Quaternion.identity;
			leg.transform.localScale    = Vector3.one;

			var rig = leg.AddComponent<Rig>();
			rig.weight = 1.0f;
			rigBuilder.layers.Add(new RigLayer(rig));

			var contraint = new GameObject($"{name}Constraint");
			contraint.transform.SetParent(leg.transform);
			contraint.transform.localPosition = Vector3.zero;
			contraint.transform.localRotation = Quaternion.identity;
			contraint.transform.localScale    = Vector3.one;
			var constraint = contraint.AddComponent<TwoBoneIKConstraint>();

			constraint.data.root   = module.GetBone(upperBone);
			constraint.data.mid    = module.GetBone(lowerBone);
			constraint.data.tip    = module.GetBone(footBone);
			constraint.data.target = module.GetOrAddPart(footBone, leg.transform);
			constraint.data.hint   = module.GetOrAddPart(lowerBone, leg.transform);

			if (constraint.data.root)
				constraint.data.root.gameObject.GetOrAddComponent<RigTransform>();
			if (constraint.data.mid)
				constraint.data.mid.gameObject.GetOrAddComponent<RigTransform>();
			if (constraint.data.tip)
				constraint.data.tip.gameObject.GetOrAddComponent<RigTransform>();

			constraint.data.targetPositionWeight = 1.0f;
			constraint.data.targetRotationWeight = 1.0f;
			constraint.data.hintWeight           = 1.0f;
		}

		private static void CreateLeftToe(RigBuilderAvatarModule module, RB rigBuilder)
			=> CreateToe(
				GetRigFromBone(HumanBodyBones.LeftToes),
				HumanBodyBones.LeftToes,
				module, rigBuilder
			);

		private static void CreateRightToe(RigBuilderAvatarModule module, RB rigBuilder)
			=> CreateToe(
				GetRigFromBone(HumanBodyBones.RightToes),
				HumanBodyBones.RightToes,
				module, rigBuilder
			);

		private static void CreateToe(string name, HumanBodyBones toeBone, RigBuilderAvatarModule module, RB rigBuilder) {
			var toe = new GameObject(name);
			toe.transform.SetParent(rigBuilder.transform);
			toe.transform.localPosition = Vector3.zero;
			toe.transform.localRotation = Quaternion.identity;
			toe.transform.localScale    = Vector3.one;

			var rig = toe.AddComponent<Rig>();
			rig.weight = 1.0f;
			rigBuilder.layers.Add(new RigLayer(rig));

			var contraint = new GameObject($"{name}Constraint");
			contraint.transform.SetParent(toe.transform);
			contraint.transform.localPosition = Vector3.zero;
			contraint.transform.localRotation = Quaternion.identity;
			contraint.transform.localScale    = Vector3.one;
			var constraint = contraint.AddComponent<DampedTransform>();

			constraint.data.constrainedObject = module.GetBone(toeBone);
			constraint.data.sourceObject      = module.GetOrAddPart(toeBone, toe.transform);

			if (constraint.data.constrainedObject)
				constraint.data.constrainedObject.gameObject.GetOrAddComponent<RigTransform>();

			constraint.data.dampPosition = 0.1f;
			constraint.data.dampRotation = 0.1f;
		}

		public static string GetRigFromBone(HumanBodyBones bone)
			=> bone switch {
				HumanBodyBones.Head
					or HumanBodyBones.Chest
					or HumanBodyBones.Neck
					or HumanBodyBones.Spine
					or HumanBodyBones.Hips
					=> "RigIK_Spine",
				HumanBodyBones.LeftUpperArm
					or HumanBodyBones.LeftLowerArm
					or HumanBodyBones.LeftHand
					=> "RigIK_LeftHand",
				HumanBodyBones.RightUpperArm
					or HumanBodyBones.RightLowerArm
					or HumanBodyBones.RightHand
					=> "RigIK_RightHand",
				HumanBodyBones.LeftUpperLeg
					or HumanBodyBones.LeftLowerLeg
					or HumanBodyBones.LeftFoot
					=> "RigIK_LeftFoot",
				HumanBodyBones.RightUpperLeg
					or HumanBodyBones.RightLowerLeg
					or HumanBodyBones.RightFoot
					=> "RigIK_RightFoot",
				HumanBodyBones.LeftToes
					=> "RigIK_LeftToe",
				HumanBodyBones.RightToes
					=> "RigIK_RightToe",
				_
					=> null
			};
	}
}
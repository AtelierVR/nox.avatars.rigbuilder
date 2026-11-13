using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Avatars.Rigging;
using Nox.CCK.Avatars.Rigging.Parameters;
using UnityEngine;
using Transform = UnityEngine.Transform;
using RB = UnityEngine.Animations.Rigging.RigBuilder;
using Nox.CCK;
using UnityEngine.Animations.Rigging;
using System;
using Nox.Avatars.Rigging;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Avatars.RigBuilder {
	public class RigBuilderRig : BaseRigging {
		private RB _rig;

		/// <summary>Bones resolved once: the humanoid mapping never changes for a rig.</summary>
		private readonly Dictionary<HumanBodyBones, Transform> _bones = new();

		// ReSharper disable Unity.PerformanceAnalysis
		public RB GetRig() {
			if (_rig)
				return _rig;

			_rig = Descriptor?.Anchor
				? Descriptor.Anchor.GetComponent<RB>()
				: null;

			Logger.LogDebug($"[Rig:{Id}] RigBuilder resolved on '{Descriptor?.Anchor?.name}' (found: {_rig != null}, layers: {_rig?.layers?.Count ?? 0}).");

			if (_rig)
				ReapplyActive();

			return _rig;
		}

		public override bool SetupParameters(BaseRigging m) {
			if (m is not RigBuilderRig module)
				return false;

			var rig = module.GetRig();
			if (!rig || rig.layers == null) return false;

			foreach (var layer in rig.layers) {
				if (!layer.rig) continue;

				var layerName = layer.rig.name;
				var group = layerName.StartsWith("RigIK_")
					? layerName[6..]
					: layerName;

				module.Parameters.Add(new RigBuilderLayerWeightParameter($"Rig/Layers/{group}/Weight", layerName, rig));
				module.Parameters.Add(new RigBuilderLayerActiveParameter($"Rig/Layers/{group}/Enabled", layerName, rig));

				module.Parameters.Add(new IKWeightParameter($"Rig/Ik/{group}/PositionWeight", layerName, IKWeightParameter.WeightType.Position, rig));
				module.Parameters.Add(new IKWeightParameter($"Rig/Ik/{group}/RotationWeight", layerName, IKWeightParameter.WeightType.Rotation, rig));
				module.Parameters.Add(new IKWeightParameter($"Rig/Ik/{group}/HintWeight", layerName, IKWeightParameter.WeightType.Hint, rig));
			}

			return true;
		}

		protected override bool GetActive(HumanBodyBones bone) {
			var rig = GetRig();
			if (!rig) return false;
			var n = RigBuilderRigGenerator.GetRigFromBone(bone);
			return (from layer in rig.layers
					where layer.rig && layer.rig.name == n
					select layer.active)
				.FirstOrDefault();
		}

		protected override void ApplyActive(HumanBodyBones bone, bool active) {
			var rig = GetRig();
			if (!rig) return;
			var n = RigBuilderRigGenerator.GetRigFromBone(bone);
			foreach (var layer in rig.layers) {
				if (!layer.rig || layer.rig.name != n) continue;
				layer.active = active;
				return;
			}
		}

		public Transform GetOrAddPart(HumanBodyBones bone, Transform tf) {
			var part = GetPart(bone);
			if (part) return part;

			var tr = GetBone(bone);
			if (!tr) return null;
			var p = new GameObject($"IK_{bone}").transform;
			p.SetParent(tf ?? Descriptor.Anchor.transform, false);
			p.position = tr.position;
			p.rotation = tr.rotation;
			p.localScale = Vector3.one;

			SetPart(bone, p);

			return p;
		}

		/// <summary>
		/// Re-points the constraint of a rig slot on an external target: the constraint then reads
		/// the tracked part instead of the target the rig generated.
		/// <para>
		/// The generated target is kept, so unbinding restores it: a constraint must never be left
		/// without a target.
		/// </para>
		/// </summary>
		protected override void ApplyTarget(HumanBodyBones bone, Transform target) {
			var entry = FindConstraint(bone);
			if (entry.Constraint == null)
				return;

			var generated = GetPart(bone);
			if (!generated)
				return;

			var solverTarget = target ? target : generated;

			switch (entry.Constraint) {
				case TwoBoneIKConstraint twoBone:
					if (entry.IsHint)
						twoBone.data.hint = solverTarget;
					else
						twoBone.data.target = solverTarget;
					break;
				case DampedTransform damped:
					damped.data.sourceObject = solverTarget;
					break;
			}
		}

		/// <summary>
		/// Target the constraint reads for a bone: the external one when it is bound, the generated
		/// one otherwise.
		/// <para>
		/// The constraint is the source of truth of the binding: the rig keeps no second copy of it.
		/// </para>
		/// </summary>
		public override Transform GetTarget(HumanBodyBones bone) {
			var entry = FindConstraint(bone);
			if (entry.Constraint == null)
				return GetPart(bone);

			return entry.Constraint switch {
				TwoBoneIKConstraint twoBone
					=> entry.IsHint ? twoBone.data.hint : twoBone.data.target,
				DampedTransform damped
					=> damped.data.sourceObject,
				_ => GetPart(bone)
			};
		}

		/// <summary>
		/// Constraint that carries the target of a bone, and whether the bone owns the target slot
		/// or the hint slot of that constraint.
		/// <para>
		/// The index is a lookup of the solver structure, not a copy of the binding: the binding
		/// itself stays in the constraint, where the solver reads it.
		/// </para>
		/// </summary>
		private readonly Dictionary<HumanBodyBones, (IRigConstraint Constraint, bool IsHint)> _constraints = new();

		/// <summary>
		/// Constraint carrying the target of a bone, resolved once.
		/// </summary>
		private (IRigConstraint Constraint, bool IsHint) FindConstraint(HumanBodyBones bone) {
			if (_constraints.TryGetValue(bone, out var cached))
				return cached;

			var generated = GetPart(bone);
			if (!generated)
				return default;

			foreach (var constraint in GetConstraints()) {
				switch (constraint) {
					case TwoBoneIKConstraint twoBone:
						if (twoBone.data.target == generated) {
							_constraints[bone] = (twoBone, false);
							return _constraints[bone];
						}

						if (twoBone.data.hint == generated) {
							_constraints[bone] = (twoBone, true);
							return _constraints[bone];
						}
						break;
					case DampedTransform damped:
						if (damped.data.sourceObject == generated) {
							_constraints[bone] = (damped, false);
							return _constraints[bone];
						}
						break;
				}
			}

			return default;
		}

		/// <summary>
		/// Constraints of the rig, in the children of the anchor (the generator parents them to the
		/// <c>RigBuilder</c> component).
		/// </summary>
		private IRigConstraint[] GetConstraints()
			=> Descriptor?.Anchor != null
				? Descriptor.Anchor.GetComponentsInChildren<IRigConstraint>(true)
				: Array.Empty<IRigConstraint>();

		/// <summary>
		/// Applies the tracked pose of every rig slot on its bone, after the animation.
		/// <para>
		/// Animation Rigging cannot drive these bones here: the avatar ships a full AnimatorController
		/// (<c>VRCFury Base</c>) and <c>PlayableAvatarModule</c> adds its own playable graph, so the Animator
		/// ends up with several animation outputs and the animated pose always wins over the graph
		/// <c>RigBuilder</c> built (measured: spine constraint active, weight 1, target moved half a metre
		/// online and no bone of the chain moved at all). The chains are therefore never solved by the
		/// constraints, and only the bones the rig writes move.
		/// </para>
		/// <para>
		/// This is the equivalent of what VRIK does for the FinalIK backend: the backend writes its own bones
		/// from <c>LateUpdate</c>, once the animation is final. Weighted by the same rig parameters the
		/// controller uses, and gated by the avatar's tracking channel, so a state that hands a limb back to
		/// the animation (calibration pose) keeps the animated bone.
		/// </para>
		/// </summary>
		public override void LateUpdate() {
			var animator = Descriptor?.Animator;
			if (!animator)
				return;

			for (var i = 0; i < DrivenBones.Length; i++)
				Drive(animator, DrivenBones[i]);
		}

		/// <summary>
		/// Bones the rig can drive, i.e. the slots the generator created a target for. A bone without a slot has no
		/// target and is skipped by <see cref="Drive"/>.
		/// </summary>
		private static readonly HumanBodyBones[] DrivenBones = {
			HumanBodyBones.Head,
			HumanBodyBones.LeftHand,
			HumanBodyBones.RightHand,
			HumanBodyBones.LeftFoot,
			HumanBodyBones.RightFoot,
		};

		/// <summary>
		/// Writes the tracked pose of one bone on the bone itself, after the animation.
		/// <para>
		/// The bone follows the target of its slot when the avatar asked for it (a <c>TrackingControl</c> in
		/// <c>Tracking</c> mode), and keeps the animated pose otherwise: a pose state that sets the parts to
		/// <c>Animation</c> (calibration T-pose) is exactly that, and it is why nothing overwrites the pose anymore.
		/// The head is the exception — its part is written by the local controller on every platform (camera in
		/// desktop, HMD in VR), so it follows it as long as the controller keeps the bone active.
		/// </para>
		/// </summary>
		private void Drive(Animator animator, HumanBodyBones bone) {
			var tr = Bone(animator, bone);
			if (!tr)
				return;

			var target = GetTarget(bone);
			if (!target || target == tr)
				return;

			switch (GetTracking(bone)) {
				case RiggingTrackingMode.Animation:
					return;
				case RiggingTrackingMode.Normal when !HasControllerActive(bone) && !(bone == HumanBodyBones.Head && IsActive(bone)):
					return;
			}

			var group = Group(bone);
			var rotationWeight = GetParameter($"Rig/Ik/{group}/RotationWeight")?.Value.ToFloat() ?? 1f;
			var positionWeight = GetParameter($"Rig/Ik/{group}/PositionWeight")?.Value.ToFloat() ?? 0f;

			if (rotationWeight > 0f)
				tr.rotation = target.rotation;
			if (positionWeight > 0f)
				tr.position = target.position;
		}

		private Transform Bone(Animator animator, HumanBodyBones bone) {
			if (_bones.TryGetValue(bone, out var cached))
				return cached;

			var found = animator.GetBoneTransform(bone);
			if (found)
				_bones[bone] = found;

			return found;
		}

		/// <summary>Parameter group of a bone: the name of its rig layer without the `RigIK_` prefix.</summary>
		private static string Group(HumanBodyBones bone) {
			var layer = RigBuilderRigGenerator.GetRigFromBone(bone);
			return layer.StartsWith("RigIK_") ? layer[6..] : layer;
		}
	}
}

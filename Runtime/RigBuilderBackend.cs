using System.Collections.Generic;
using Nox.Avatars.Rigging;
using UnityEngine;
using Nox.CCK.Avatars.Rigging;
using Nox.CCK.Utils;

namespace Nox.Avatars.RigBuilder {
	/// <summary>
	/// Rigging backend that uses Unity's Animation Rigging (RigBuilder) package.
	/// Register this backend from the <c>nox.avatars.rigbuilder</c> mod entry point.
	/// </summary>
	public class RigBuilderBackend : IRiggingBackend {
		public const string BACKEND_ID = "rigbuilder";

		public string Id
			=> BACKEND_ID;

		/// <inheritdoc/>
		/// RigBuilder is always available — it has no external dependencies. Returns 0 for any arguments.
		public int CanHandle(IRuntimeAvatar runtime)
			=> 0;

		/// <inheritdoc/>
		public IRiggingModule Instantiate(IRuntimeAvatar runtime)
			=> runtime.Descriptor.Anchor.GetOrAddComponent<RigBuilderAvatarModule>();

		/// <inheritdoc/>
		public void SetupRig(IRiggingModule module) {
			if (module is not RigBuilderAvatarModule rig)
				throw new System.ArgumentException($"Expected module of type {nameof(RigBuilderAvatarModule)}, got {module.GetType().Name}");
			RigBuilderRigGenerator.Create(rig);
		}

		/// <inheritdoc/>
		public void Dispose() { }
	}
}
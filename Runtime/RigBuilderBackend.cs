using Nox.Avatars.Rigging;
using Nox.CCK.Utils;
using Logger = Nox.CCK.Utils.Logger;

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
		public IRiggingModule Instantiate(IRuntimeAvatar runtime) {
			var module = runtime.Descriptor.Anchor.GetOrAddComponent<RigBuilderAvatarModule>();
			
			if (!module.Before(runtime)) {
				Logger.LogError("Failed to initialize with the given runtime arguments.", module, nameof(RigBuilderBackend));
				module.enabled = false;
				return null;
			}

			RigBuilderRigGenerator.Create(module, runtime);

			if (!module.After(runtime)) {
				Logger.LogError("Failed to finalize setup with the given runtime arguments.", module, nameof(RigBuilderBackend));
				module.enabled = false;
				return null;
			}
			
			return module;
		}

		/// <inheritdoc/>
		public void Dispose() { }
	}
}
using Nox.Avatars.Rigging;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Initializers;

namespace Nox.Avatars.RigBuilder.Runtime {
	/// <summary>
	/// Entry point for the <c>nox.avatars.rigbuilder</c> mod.
	/// Registers <see cref="RigBuilderBackend"/> with the <see cref="IRiggingBackendRegistry"/>
	/// exposed by <c>nox.avatars.modules</c>, and unregisters it on dispose.
	/// </summary>
	public class Main : IMainModInitializer {
		private IMainModCoreAPI        _api;
		private RigBuilderBackend _backend;

        private IRiggingBackendRegistry Registry 
            => _api.ModAPI
            .GetMod("avatars.modules")
            .GetInstance<IRiggingBackendRegistry>();

		public void OnInitializeMain(IMainModCoreAPI api) {
			_api      = api;
			_backend = new RigBuilderBackend();
			Registry.Register(_backend);
			api.LoggerAPI.LogDebug("RigBuilder backend registered.");
		}

		public void OnDisposeMain() {
			Registry?.Unregister(_backend);
            _backend.Dispose();
			_backend = null;
			_api      = null;
		}
	}
}

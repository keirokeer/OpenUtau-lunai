using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace OpenUtau.Core.Util {
    /// <summary>
    /// Loads external phonemizer/batch-edit plugins while sharing the host's OpenUtau.Core.
    /// Assembly.LoadFile isolates dependencies and can bind plugins to a different Core DLL
    /// (e.g. a leftover copy in a nested install folder), causing MissingFieldException when
    /// API field types change (int vs int? toneShift).
    /// </summary>
    internal sealed class PluginLoadContext : AssemblyLoadContext {
        static readonly string[] SharedAssemblyNames = {
            "OpenUtau.Core",
            "OpenUtau.Plugin.Builtin",
        };

        readonly AssemblyDependencyResolver resolver;

        PluginLoadContext(string pluginPath) : base(isCollectible: false) {
            resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName) {
            if (assemblyName.Name != null &&
                SharedAssemblyNames.Any(n => n.Equals(assemblyName.Name, StringComparison.OrdinalIgnoreCase))) {
                // Return null so the default context's already-loaded host assemblies are used.
                return null;
            }
            string? path = resolver.ResolveAssemblyToPath(assemblyName);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
            string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }

        public static Assembly LoadPlugin(string path) {
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (SharedAssemblyNames.Any(n => n.Equals(fileName, StringComparison.OrdinalIgnoreCase))) {
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name != null &&
                        a.GetName().Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                if (existing != null) {
                    return existing;
                }
                // Host Builtin/Core: load into the default context, never a private copy.
                return Assembly.LoadFrom(path);
            }
            var context = new PluginLoadContext(path);
            return context.LoadFromAssemblyPath(path);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Composition.Hosting;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Moe.Mottomo.ArcaeaSim.Configuration;
using OpenMLTD.MilliSim.Core;
using OpenMLTD.MilliSim.Foundation;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Moe.Mottomo.ArcaeaSim.Subsystems.Plugin {
    /// <inheritdoc cref="BasePluginManager"/>
    /// <summary>
    /// Plugin manager for ArcaeaSim.
    /// </summary>
    internal sealed class ArcaeaSimPluginManager : BasePluginManager {

        /// <summary>
        /// Creates a new instance of <see cref="ArcaeaSimPluginManager"/>.
        /// </summary>
        internal ArcaeaSimPluginManager() {
            AppDomain.CurrentDomain.AssemblyResolve += AppDomain_Resolve;
            InstancingFactoryIDs = new string[0];
        }

        /// <summary>
        /// Loaded plugin list. Can be used in diagnostic windows, for example a <see cref="System.Windows.Forms.ListView"/> listing plugin details.
        /// </summary>
        public override IReadOnlyList<IMilliSimPlugin> Plugins => _allPlugins;

        /// <summary>
        /// The IDs of the factories that will be used for instancing responsible classes.
        /// </summary>
        [NotNull, ItemNotNull]
        internal IReadOnlyList<string> InstancingFactoryIDs { get; private set; }

        /// <summary>
        /// Scan and load plugins according to default plugin list file.
        /// </summary>
        internal void LoadPlugins() {
            LoadPlugins(DefaultPluginListFile);
        }

        /// <summary>
        /// Scan and load plugins according to specified plugin list file.
        /// </summary>
        /// <param name="pluginListFilePath">Path to the plugin list file.</param>
        internal void LoadPlugins([NotNull] string pluginListFilePath) {
            var deserializerBuilder = new DeserializerBuilder();
            var deserializer = deserializerBuilder
                .IgnoreUnmatchedProperties()
                .WithNamingConvention(new UnderscoredNamingConvention())
                .Build();

            PluginsConfig pluginsConfig;

            using (var fileStream = File.Open(pluginListFilePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                using (var reader = new StreamReader(fileStream)) {
                    pluginsConfig = deserializer.Deserialize<PluginsConfig>(reader);
                }
            }

            InstancingFactoryIDs = pluginsConfig.ComponentFactories;

            string[] filters;

            switch (pluginsConfig.Loading.Mode) {
                case PluginSearchMode.Default:
                    filters = null;
                    break;
                case PluginSearchMode.Exclusive:
                    filters = pluginsConfig.Loading.Lists.BlackList;
                    break;
                case PluginSearchMode.Inclusive:
                    filters = pluginsConfig.Loading.Lists.WhiteList;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var startupPath = ApplicationHelper.StartupPath;
            var searchPaths = new string[SubSearchPaths.Count + 1];

            searchPaths[0] = startupPath;

            for (var i = 1; i <= SubSearchPaths.Count; ++i) {
                searchPaths[i] = Path.Combine(startupPath, SubSearchPaths[i - 1]);
            }

            LoadAssemblies(pluginsConfig.Loading.Mode, filters, searchPaths);
        }

        protected override void Dispose(bool disposing) {
            _extensionContainer?.Dispose();
            _extensionContainer = null;
            _extensionConfiguration = null;

            if (Plugins != null) {
                foreach (var plugin in Plugins) {
                    if (plugin is IDisposable d) {
                        d.Dispose();
                    }
                }
            }

            AppDomain.CurrentDomain.AssemblyResolve -= AppDomain_Resolve;
        }

        private void LoadAssemblies(PluginSearchMode searchMode, [CanBeNull, ItemNotNull] string[] filters, [NotNull, ItemNotNull] params string[] searchPaths) {
            if (_extensionContainer != null) {
                _extensionContainer.Dispose();
                _extensionContainer = null;
            }

            var allAssemblies = FindAssemblies(searchMode, filters, searchPaths);
            var configuration = new ContainerConfiguration().WithAssemblies(allAssemblies);
            var host = configuration.CreateContainer();

            var loadedPlugins = host.GetExports<IMilliSimPlugin>().ToArray();
            _allPlugins = loadedPlugins;

            _extensionConfiguration = configuration;
            _extensionContainer = host;
        }

        private static IReadOnlyList<Assembly> FindAssemblies(PluginSearchMode searchMode, [CanBeNull, ItemNotNull] string[] filters, [NotNull, ItemNotNull] params string[] searchPaths) {
            var allAssemblies = new List<Assembly>();
            var fullFilters = filters?.Where(f => !string.IsNullOrEmpty(f)).Select(Path.GetFullPath).ToArray() ?? new string[0];

            var isModernWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

            foreach (var directory in searchPaths) {
                if (!Directory.Exists(directory)) {
                    continue;
                }

                var assemblyFileNames = Directory
                    .EnumerateFiles(directory)
                    .Where(str => str.ToLowerInvariant().EndsWith(".dll"));

                foreach (var assemblyFileName in assemblyFileNames) {
                    bool PathEquals(string filter) {
                        var compareFlag = isModernWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                        return string.Equals(filter, assemblyFileName, compareFlag);
                    }

                    if (fullFilters.Length > 0) {
                        switch (searchMode) {
                            case PluginSearchMode.Default:
                                break;
                            case PluginSearchMode.Exclusive:
                                if (Array.Exists(fullFilters, PathEquals)) {
                                    continue;
                                }
                                break;
                            case PluginSearchMode.Inclusive:
                                if (!Array.Exists(fullFilters, PathEquals)) {
                                    continue;
                                }
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(nameof(searchMode), searchMode, null);
                        }
                    }

                    try {
                        var assembly = Assembly.LoadFrom(assemblyFileName);
                        allAssemblies.Add(assembly);
                    } catch (Exception ex) {
                        Debug.Print(ex.Message);
                    }
                }
            }

            return allAssemblies;
        }

        private static Assembly AppDomain_Resolve(object sender, ResolveEventArgs e) {
            var currentPath = ApplicationHelper.StartupPath;
            var assemblyFileName = e.Name + ".dll";

            var assemblyPath = Path.Combine(currentPath, assemblyFileName);

            // Search the root directory first...
            if (File.Exists(assemblyPath)) {
                return Assembly.LoadFrom(assemblyPath);
            }

            // ... and then the specified sub directories (e.g. "plugins") if they exist.
            foreach (var subPath in SubSearchPaths) {
                assemblyPath = Path.Combine(currentPath, subPath, assemblyFileName);

                if (File.Exists(assemblyPath)) {
                    return Assembly.LoadFrom(assemblyFileName);
                }
            }

            return null;
        }

        private static readonly IReadOnlyList<string> SubSearchPaths = new[] {
            "plugins"
        };

        private const string DefaultPluginListFile = "Contents/plugins.yml";

        private ContainerConfiguration _extensionConfiguration;
        private CompositionHost _extensionContainer;
        private IReadOnlyList<IMilliSimPlugin> _allPlugins;

    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Description of the build extent that contains only a set of <see cref="ModuleDefinition"/>.
    /// </summary>
    /// <remarks>
    /// Unlike regular <see cref="Workspace"/>, workspace definition has just a list of files that denotes the build.
    /// Files are not parsed yet.
    /// Workspace definition is required for incremental scenarios when the front end parses just part of the workspace.
    /// Definition does not necessarily has the entire set of files/modules required by the final build.
    /// It is possible that some modules would be resolved at runtime during parsing time. For instance, some modules
    /// could be downloaded by resolver on demand.
    /// Another case, why some modules/spec could be missing is filtering: it is possible that the workspace definition
    /// would be filtered and will contain only required set of modules and all other modules would be added to a real workspace
    /// because they're needed.
    ///
    /// Note, that the workspace definition will not contain main configuration file and module configuration files.
    /// </remarks>
    public sealed class WorkspaceDefinition
    {
        private readonly Dictionary<AbsolutePath, ModuleDefinition> m_moduleDefinitionsBySpecPath;

        /// <summary>
        /// Set of modules for the current workspace.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        public IReadOnlyCollection<ModuleDefinition> Modules { get; }

        /// <summary>
        /// Set of specs with an owning modules.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        public IReadOnlyList<SpecWithOwningModule> Specs { get; }

        /// <summary>
        /// Number of specs in the current workspace.
        /// </summary>
        public int SpecCount => Specs.Count;

        /// <summary>
        /// Number of modules in the current workspace.
        /// </summary>
        public int ModuleCount => Modules.Count;

        /// <nodoc />
        public ModuleDefinition PreludeModule { get; }

        /// <nodoc/>
        public WorkspaceDefinition(IEnumerable<ModuleDefinition> modules, ModuleDefinition preludeModule)
        {
            Contract.Requires(modules != null);
            Contract.RequiresForAll(modules, m => m != null);

            Modules = modules.ToArray();
            PreludeModule = preludeModule;

            m_moduleDefinitionsBySpecPath = CreateModuleDefinitionMapBySpec(modules);
            Specs = GetSpecsWithOwningModules(modules);
        }

        /// <nodoc />
        public ModuleDefinition TryGetModuleDefinition(AbsolutePath spec)
        {
            m_moduleDefinitionsBySpecPath.TryGetValue(spec, out ModuleDefinition result);
            return result;
        }

        /// <nodoc />
        public bool IsPreludeSpec(AbsolutePath path)
        {
            return TryGetModuleDefinition(path)?.Equals(PreludeModule) == true;
        }

        private static Dictionary<AbsolutePath, ModuleDefinition> CreateModuleDefinitionMapBySpec(IEnumerable<ModuleDefinition> modules)
        {
            var result = new Dictionary<AbsolutePath, ModuleDefinition>();
            foreach (var module in modules)
            {
                foreach (var spec in module.Specs)
                {
                    result[spec] = module;
                }
            }

            return result;
        }

        private static IReadOnlyList<SpecWithOwningModule> GetSpecsWithOwningModules(IEnumerable<ModuleDefinition> modules)
        {
            var result = new List<SpecWithOwningModule>();
            foreach (var module in modules)
            {
                foreach (var spec in module.Specs)
                {
                    result.Add(new SpecWithOwningModule(spec, module));
                }
            }

            return result;
        }
    }
}

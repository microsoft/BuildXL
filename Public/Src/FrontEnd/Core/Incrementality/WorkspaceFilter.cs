// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Core.Incrementality
{
    /// <summary>
    /// Applies filter to a given <see cref="Workspace"/> instance to produce the filtered workspace.
    /// </summary>
    public sealed class WorkspaceFilter
    {
        private readonly PathTable m_pathTable;

        /// <nodoc />
        public WorkspaceFilter([NotNull]PathTable pathTable)
        {
            m_pathTable = pathTable;
        }

        /// <summary>
        /// Filter workspace and returns a list of module definitions required for evaluation.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="FilterForConversion"/> this function returns a minimal set of modules that directly
        /// satisfy a given filter.
        /// This means that the list returned from this function is a subset of the result returned from <see cref="FilterForConversion"/>
        /// because latter return a transitive closure of files and modules.
        /// </remarks>
        public List<ModuleDefinition> FilterForEvaluation([NotNull] Workspace workspace, [NotNull] EvaluationFilter evaluationFilter)
        {
            // First, need to get all modules that satisfy a given filter.
            var modulesToInclude = new HashSet<ModuleDefinition>();

            var modulesToResolve = new HashSet<string>(evaluationFilter.ModulesToResolve.Select(m => m.ToString(m_pathTable.StringTable)));

            foreach (var module in workspace.SpecModules)
            {
                if (modulesToResolve.Contains(module.Descriptor.Name))
                {
                    modulesToInclude.Add(module.Definition);
                }
            }

            // Then need to find all specs. But, instead of keeping them in a list, let's store them in a map
            // to simplify construction of final modules.
            var partiallyFilteredModules = new Dictionary<ModuleDefinition, Dictionary<AbsolutePath, ISourceFile>>();
            foreach (var kvp in workspace.SpecSources)
            {
                var parsedSpec = kvp.Key;

                // If the spec belonged to a module that is required, then the entire module will be evaluated.
                // No need to keep the spec separately
                if (!modulesToInclude.Contains(kvp.Value.OwningModule.Definition))
                {
                    foreach (var specRootToResolve in evaluationFilter.ValueDefinitionRootsToResolve)
                    {
                        if (parsedSpec == specRootToResolve || parsedSpec.IsWithin(m_pathTable, specRootToResolve))
                        {
                            // File is not part of 'must have' module and is part of 'must have' spec.
                            var map = partiallyFilteredModules.GetOrAdd(
                                kvp.Value.OwningModule.Definition,
                                k => new Dictionary<AbsolutePath, ISourceFile>());
                            map[kvp.Key] = kvp.Value.SourceFile;
                        }
                    }
                }
            }

            foreach (var kvp in partiallyFilteredModules)
            {
                // Need to recreate both - module definition and parsed module,
                // becase the set of specs is different.
                var moduleDefinition = kvp.Key.WithSpecs(kvp.Value.Keys.ToReadOnlySet());
                modulesToInclude.Add(moduleDefinition);
            }

            return modulesToInclude.ToList();
        }

        /// <summary>
        /// Filter a workspace definition.
        /// </summary>
        public List<ModuleDefinition> FilterWorkspaceDefinition(
            [NotNull] WorkspaceDefinition workspace,
            [NotNull] EvaluationFilter evaluationFilter,
            [NotNull] ISpecDependencyProvider provider)
        {
            // Resulting list should always have a prelude.
            var modulesToInclude = new HashSet<ModuleDefinition> { workspace.PreludeModule };

            // Getting all the files relevant to the build.
            var filesToInclude = GetFilesToInclude(modulesToInclude, workspace, provider, evaluationFilter);

            // Keep modules with a set of files relevant for a given filter.
            var partiallyFilteredModules = new Dictionary<ModuleDefinition, HashSet<AbsolutePath>>();
            foreach (var kvp in workspace.Specs)
            {
                // File is not part of 'must have' module and is part of 'must have' spec.
                if (filesToInclude.Contains(kvp.Path))
                {
                    var map = partiallyFilteredModules.GetOrAdd(kvp.OwningModule, k => new HashSet<AbsolutePath>());
                    map.Add(kvp.Path);
                }
            }

            foreach (var kvp in partiallyFilteredModules)
            {
                var moduleDefinition = kvp.Key.WithSpecs(kvp.Value.ToReadOnlySet());
                modulesToInclude.Add(moduleDefinition);
            }

            return modulesToInclude.ToList();
        }

        private HashSet<AbsolutePath> GetFilesToInclude(HashSet<ModuleDefinition> modulesToInclude, WorkspaceDefinition workspace, ISpecDependencyProvider provider, EvaluationFilter evaluationFilter)
        {
            var filesToInclude = new HashSet<AbsolutePath>();
            foreach (var kvp in workspace.Specs)
            {
                var specPath = kvp.Path;

                // If the spec belonged to a module that is required, then the entire module will be evaluated.
                // No need to keep the spec separately
                if (!modulesToInclude.Contains(kvp.OwningModule))
                {
                    foreach (var specRootToResolve in evaluationFilter.ValueDefinitionRootsToResolve)
                    {
                        if (specPath == specRootToResolve || specPath.IsWithin(m_pathTable, specRootToResolve))
                        {
                            filesToInclude.Add(specPath);
                            AddUpStreamDependencies(filesToInclude, specPath, provider);
                        }
                    }
                }
            }

            return filesToInclude;
        }

        /// <summary>
        /// Filter a workspace and returns a list of parsed module that satisfy a given filter based on file-2-file dependencies.
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        public List<ParsedModule> FilterForConversion([NotNull]Workspace workspace, [NotNull]EvaluationFilter evaluationFilter)
        {
            // TODO: need to check that file2file map is available and skip filtering otherwise.
            var spec2SpecMapProvider = new WorkspaceBasedSpecDependencyProvider(workspace, m_pathTable);

            // First, getting all modules, that satisfy the module filter.
            var modulesToInclude = GetModulesToInclude(workspace, spec2SpecMapProvider, evaluationFilter);

            // Second, getting all the specs, that satisfy the spec filter.
            var filesToInclude = GetFilesToInclude(
                modulesToInclude,
                workspace,
                spec2SpecMapProvider,
                evaluationFilter);

            // Third, constructing a new set of modules based on a filtered set of specs.
            var partiallyFilteredModules = new Dictionary<ModuleDefinition, Dictionary<AbsolutePath, ISourceFile>>();
            foreach (var kvp in workspace.SpecSources)
            {
                // File is not part of 'must have' module and is part of 'must have' spec.
                if (!modulesToInclude.Contains(kvp.Value.OwningModule) && filesToInclude.Contains(kvp.Key))
                {
                    var map = partiallyFilteredModules.GetOrAdd(kvp.Value.OwningModule.Definition, k => new Dictionary<AbsolutePath, ISourceFile>());
                    map[kvp.Key] = kvp.Value.SourceFile;
                }
            }

            foreach (var kvp in partiallyFilteredModules)
            {
                // Need to recreate both - module definition and parsed module,
                // because the set of specs is different.
                var moduleDefinition = kvp.Key.WithSpecs(kvp.Value.Keys.ToReadOnlySet());
                var parsedPartialModule = new ParsedModule(moduleDefinition, kvp.Value, workspace.GetModuleByModuleDescriptor(moduleDefinition.Descriptor).ReferencedModules);
                modulesToInclude.Add(parsedPartialModule);
            }

            return modulesToInclude.ToList();
        }

        private HashSet<AbsolutePath> GetFilesToInclude(HashSet<ParsedModule> modulesToInclude, Workspace workspace, ISpecDependencyProvider provider, EvaluationFilter evaluationFilter)
        {
            // TODO: merge two 'GetFilesToInclude' methods into one.
            var filesToInclude = new HashSet<AbsolutePath>();
            foreach (var kvp in workspace.SpecSources)
            {
                var parsedSpec = kvp.Key;

                // If the spec belonged to a module that is required, then the entire module will be evaluated.
                // No need to keep the spec separately
                if (!modulesToInclude.Contains(kvp.Value.OwningModule))
                {
                    foreach (var specRootToResolve in evaluationFilter.ValueDefinitionRootsToResolve)
                    {
                        if (parsedSpec == specRootToResolve || parsedSpec.IsWithin(m_pathTable, specRootToResolve))
                        {
                            filesToInclude.Add(parsedSpec);
                            AddUpStreamDependencies(filesToInclude, kvp.Key, provider);
                        }
                    }
                }
            }

            return filesToInclude;
        }

        private static void AddUpStreamDependencies(
            HashSet<AbsolutePath> resultingSet,
            AbsolutePath specPath,
            ISpecDependencyProvider provider)
        {
            var dependencies = provider.GetFileDependenciesOf(specPath);

            foreach (var path in dependencies)
            {
                if (resultingSet.Add(path))
                {
                    // We've just added new file to a closure.
                    // Need to add their dependencies as well.
                    AddUpStreamDependencies(resultingSet, path, provider);
                }
            }
        }

        private HashSet<ParsedModule> GetModulesToInclude(Workspace workspace, ISpecDependencyProvider provider, EvaluationFilter evaluationFilter)
        {
            var modulesToResolve = new HashSet<string>(evaluationFilter.ModulesToResolve.Select(s => s.ToString(m_pathTable.StringTable)));

            // Getting all the modules specified via filter
            var modulesToInclude = workspace.SpecModules.Where(m => modulesToResolve.Contains(m.Descriptor.Name)).ToHashSet();

            // Getting all upstream dependencis for all modules.
            modulesToInclude = TransitiveClosureOfAllRelevantModules(modulesToInclude, workspace, provider);
            return modulesToInclude;
        }

        private static HashSet<string> GetUpStreamModuleNames(ParsedModule parsedModule, ISpecDependencyProvider semanticModel)
        {
            HashSet<string> upStreamDependencies = new HashSet<string>();
            foreach (var spec in parsedModule.Specs)
            {
                upStreamDependencies.AddRange(semanticModel.GetModuleDependenciesOf(spec.Key));
            }

            return upStreamDependencies;
        }

        private static HashSet<ParsedModule> TransitiveClosureOfAllRelevantModules(IEnumerable<ParsedModule> modules, Workspace workspace, ISpecDependencyProvider provider)
        {
            var modulesByName = workspace.SpecModules.ToDictionary(m => m.Descriptor.Name);
            HashSet<ParsedModule> result = new HashSet<ParsedModule>(modules);

            foreach (var module in modules)
            {
                result.AddRange(GetUpStreamModuleNames(module, provider).Select(name => modulesByName[name]));
            }

            return result;
        }

        /// <summary>
        /// Applies module filter for a given <paramref name="workspace"/>.
        /// </summary>
        public WorkspaceDefinition ApplyModuleFilter(WorkspaceDefinition workspace, IReadOnlyList<StringId> modulesToKeep)
        {
            HashSet<string> requiredModules = new HashSet<string>(modulesToKeep.Select(m => m.ToString(m_pathTable.StringTable)));

            List<ModuleDefinition> finalModules = new List<ModuleDefinition>();
            var preludeModule = workspace.PreludeModule;
            foreach (var module in workspace.Modules)
            {
                // Should keep special modules and the prelude.
                if (module.Descriptor.IsSpecialConfigModule() ||
                    module.Equals(preludeModule) ||
                    requiredModules.Contains(module.Descriptor.Name))
                {
                    finalModules.Add(module);
                }
            }

            return new WorkspaceDefinition(finalModules, workspace.PreludeModule);
        }
    }
}

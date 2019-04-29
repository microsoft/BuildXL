// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;
using Diagnostic = TypeScript.Net.Diagnostics.Diagnostic;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// General workspace-level validations. Two general validations are performed: module definition validations (right after configuration interpretation is done)
    /// and parsed module validations (performed before constructing a workspace)
    /// </summary>
    public static class WorkspaceValidator
    {
        private static readonly List<(string moduleName, string pathToModuleFile)> s_emptyReferences = new List<(string, string)>();

        /// <summary>
        /// Validates all module definitions
        /// </summary>
        /// <remarks>
        /// As of today, this validation does:
        /// - Double ownership checks
        /// - Module-to-module cycle detection
        /// </remarks>
        public static bool ValidateModuleDefinitions(HashSet<ModuleDefinition> moduleDefinitions, PathTable pathTable, out WorkspaceFailure[] failures)
        {
            Contract.Requires(moduleDefinitions != null);
            Contract.Requires(pathTable != null);

            var currentFailures = new List<WorkspaceFailure>();
            ValidateDoubleOwnership(moduleDefinitions, currentFailures, pathTable);
            ValidateNoCycles(moduleDefinitions, pathTable, currentFailures);

            failures = currentFailures.ToArray();
            return failures.Length == 0;
        }

        /// <summary>
        /// Validates all parsed modules
        /// </summary>
        /// <remarks>
        /// Checks that actual module references are contained in allowed module references
        /// </remarks>
        public static IEnumerable<WorkspaceFailure> ValidateParsedModules(ICollection<ParsedModule> parsedModules, PathTable pathTable)
        {
            Contract.Requires(parsedModules != null);
            Contract.Requires(pathTable != null);

            var failures = new List<WorkspaceFailure>();
            ValidateAllowedModuleReferences(parsedModules, pathTable, failures);
            return failures;
        }

        private static void ValidateDoubleOwnership(ICollection<ModuleDefinition> moduleDefinitions, List<WorkspaceFailure> currentFailures, PathTable pathTable)
        {
            // This is part of a larger work for cleaning up a validation logic from the resolution logic. Work item: 934651
            var specs = new Dictionary<AbsolutePath, ModuleDefinition>(moduleDefinitions.Count);
            foreach (var md in moduleDefinitions)
            {
                foreach (var spec in md.Specs)
                {
                    if (specs.TryGetValue(spec, out var currentOwner))
                    {
                        // Found double ownership.
                        currentFailures.Add(
                            WorkspaceFailure.SpecOwnedByTwoModules(
                                firstModuleName: currentOwner.Descriptor.DisplayName,
                                firstModulePath: currentOwner.Root.ToString(pathTable),
                                specFullPath: spec.ToString(pathTable),
                                secondModuleName: md.Descriptor.DisplayName,
                                secondModulePath: md.Root.ToString(pathTable)));
                    }
                    else
                    {
                        specs.Add(spec, md);
                    }
                }
            }
        }

        private static void ValidateNoCycles(ICollection<ModuleDefinition> parsedModules, PathTable pathTable, ICollection<WorkspaceFailure> failures)
        {
            var edges = ComputeAllEdges(parsedModules, pathTable);

            // The visiting chain is used for reporting purposes only.
            var visitingChain = new List<(string moduleName, string pathToFile)>();

            // The usual gray-black pattern is used to detect a cycle
            var visiting = new HashSet<string>();
            var visited = new HashSet<string>();
            foreach (var moduleWithLocation in edges.Keys)
            {
                visitingChain.Add(moduleWithLocation);
                visiting.Add(moduleWithLocation.moduleName);
                if (!ValidateNoCycleInEdges(moduleWithLocation, visitingChain, visiting, visited, edges))
                {
                    // We found a cycle, report it and abort the search
                    failures.Add(new CycleInModuleReferenceFailure(visitingChain));

                    break;
                }
            }
        }

        private static bool ValidateNoCycleInEdges(
            (string moduleName, string pathToFile) moduleWithLocation,
            IList<(string moduleName, string pathToFile)> visitingChain,
            ISet<string> visiting,
            ISet<string> visited,
            MultiValueDictionary<(string moduleName, string pathToFile), (string moduleName, string pathToFile)> allEdges)
        {
            // If the module is not referencing anybody then the references are empty
            if (!allEdges.TryGetValue(moduleWithLocation, out var directReferences))
            {
                directReferences = s_emptyReferences;
            }

            foreach (var directReferenceWithLocation in directReferences)
            {
                var referencedModule = directReferenceWithLocation.moduleName;
                if (visiting.Contains(referencedModule))
                {
                    // cycle!
                    return false;
                }

                if (visited.Contains(referencedModule))
                {
                    // Already visited this module and found no cycles from there, proceed no further
                    continue;
                }

                // No cycle so far, update the visiting structures to include the edge and move forward in a BFS manner
                visiting.Add(referencedModule);
                visitingChain.Add(directReferenceWithLocation);
                if (!ValidateNoCycleInEdges(directReferenceWithLocation, visitingChain, visiting, visited, allEdges))
                {
                    // A cycle was found. Abort the search.
                    return false;
                }
            }

            // We traversed all dependencies from the current edge, mark it as visited and remove it from visiting
            visited.Add(moduleWithLocation.moduleName);
            visiting.Remove(moduleWithLocation.moduleName);

            // Remove the edge from the visiting chain (it should always be the last one)
            visitingChain.RemoveAt(visitingChain.Count - 1);

            return true;
        }

        private static MultiValueDictionary<(string moduleName, string pathToModuleFile), (string moduleName, string pathToModuleFile)> ComputeAllEdges(ICollection<ModuleDefinition> moduleDefinitions, PathTable pathTable)
        {
            var cyclicFriendsByModuleName = moduleDefinitions.ToDictionary(
                moduleDefinition => moduleDefinition.Descriptor.Name,
                moduleDefinition => moduleDefinition.CyclicalFriendModules != null
                    ? new HashSet<string>(moduleDefinition.CyclicalFriendModules.Select(moduleReferenceWithProvenance => moduleReferenceWithProvenance.Name))
                    : null);

            var pathToModuleConfigurationByModuleName = moduleDefinitions.ToDictionary(
                moduleDefinition => moduleDefinition.Descriptor.Name,
                moduleDefinition => moduleDefinition.ModuleConfigFile.ToString(pathTable));

            var edges = new MultiValueDictionary<(string moduleName, string pathToModuleFile), (string moduleName, string pathToModuleFile)>();
            foreach (var moduleDefinition in moduleDefinitions)
            {
                // If allowed modules is not defined, then that module is off the hook
                // and no validations are performed on it regarding cycles
                if (moduleDefinition.AllowedModuleDependencies == null)
                {
                    continue;
                }

                var moduleName = moduleDefinition.Descriptor.Name;

                foreach (var referencedModule in moduleDefinition.AllowedModuleDependencies)
                {
                    // If the referenced module is not part of the list of all modules, which can happen
                    // since we are not sure we know the whole world of modules, then the module does not
                    // contribute to a cycle
                    if (!cyclicFriendsByModuleName.TryGetValue(referencedModule.Name, out var cyclicFriends))
                    {
                        continue;
                    }

                    // If cyclic friends are specified and the referenced module lists the referencing module, then the edge
                    // does not contribute to a potential cycle
                    if (cyclicFriends?.Contains(moduleName) == true)
                    {
                        continue;
                    }

                    // Otherwise we add it
                    edges.Add(
                        (moduleName, moduleDefinition.ModuleConfigFile.ToString(pathTable)),
                        (referencedModule.Name, pathToModuleConfigurationByModuleName[referencedModule.Name]));
                }
            }

            return edges;
        }

        private static void ValidateAllowedModuleReferences(IEnumerable<ParsedModule> parsedModules, PathTable pathTable, ICollection<WorkspaceFailure> failures)
        {
            foreach (var parsedModule in parsedModules)
            {
                // If allowed module dependencies is not defined, then no restrictions are enforced
                if (parsedModule.Definition.AllowedModuleDependencies == null)
                {
                    continue;
                }

                var disallowedReferencesPerSourceFile = ComputedDisallowedReferencesPerSourceFile(pathTable, parsedModule);

                // Now we create all the parsing failures
                foreach (var disallowedReferences in disallowedReferencesPerSourceFile)
                {
                    var diagnostics = disallowedReferences.Value.Select(
                        moduleDescriptorWithProvenance =>
                            Diagnostic.CreateDiagnosticAtLocation(
                                disallowedReferences.Key,
                                moduleDescriptorWithProvenance.location,
                                moduleDescriptorWithProvenance.module.Name.Length,
                                Errors.Importing_module_0_from_1_is_not_allowed_by_allowedDependencies_policy_in_module_1_located_at_2,
                                moduleDescriptorWithProvenance.module.Name,
                                parsedModule.Descriptor.Name,
                                parsedModule.Definition.ModuleConfigFile.ToString(pathTable)));

                    failures.Add(new DisallowedModuleReferenceFailure(parsedModule.Descriptor, disallowedReferences.Key, diagnostics));
                }
            }
        }

        private static Dictionary<ISourceFile, List<(ModuleDescriptor module, Location location)>> ComputedDisallowedReferencesPerSourceFile(PathTable pathTable, ParsedModule parsedModule)
        {
            Contract.Requires(parsedModule.Definition.AllowedModuleDependencies != null);

            var allowedModules = new HashSet<string>(
                parsedModule.Definition.AllowedModuleDependencies.Select(moduleReferenceWithProvenance => moduleReferenceWithProvenance.Name));

            // Here we group all disallowed references for the same source file, since we want to report all
            // disallowed module references together
            var disallowedReferencesPerSourceFile = new Dictionary<ISourceFile, List<(ModuleDescriptor module, Location location)>>();

            foreach (var (referencedModule, location) in parsedModule.ReferencedModules)
            {
                // If the referenced module is part of the allowed modules, we are good
                // For now the module version is not taken into account, since imports don't support version, so just the name is used
                if (allowedModules.Contains(referencedModule.Name))
                {
                    continue;
                }

                // A module is ilegally referenced, we track the error
                var pathToFile = AbsolutePath.Create(pathTable, location.File);
                if (!parsedModule.Specs.TryGetValue(pathToFile, out var sourceFile))
                {
                    Contract.Assert(false, I($"The path {location.File} must be part of the workspace"));
                }

                if (!disallowedReferencesPerSourceFile.TryGetValue(sourceFile, out var disallowedReferences))
                {
                    disallowedReferences = new List<(ModuleDescriptor module, Location location)>();
                    disallowedReferencesPerSourceFile[sourceFile] = disallowedReferences;
                }

                disallowedReferences.Add((referencedModule, location));
            }

            return disallowedReferencesPerSourceFile;
        }
    }
}

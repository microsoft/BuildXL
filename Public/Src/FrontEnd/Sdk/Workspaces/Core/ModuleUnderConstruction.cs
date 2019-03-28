// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// A module whose specs are being parsed. This class is thread-safe.
    /// </summary>
    public sealed class ModuleUnderConstruction
    {
        private int m_failedSpecsCount;
        private int m_parsedSpecsCount;
        private int m_isModuleFirstTimeCompleteCounter = 0;
        private readonly Dictionary<AbsolutePath, ISourceFile> m_parsedSpecs;
        private bool m_isRootQualifierDefined;
        private bool m_isTopLevelWithQualifierInjected;
        private readonly HashSet<(ModuleDescriptor, Location)> m_referencedModules;

        // We cache here the result of creating a completed (parsed) module out of a module under construction
        private bool m_isCompleteModuleCreated = false;
        private ParsedModule m_completedModule = null;

        /// <nodoc/>
        public ModuleDefinition Definition { get; }

        /// <nodoc/>
        public ModuleUnderConstruction(ModuleDefinition definition)
        {
            Contract.Requires(definition != null);

            Definition = definition;
            m_isRootQualifierDefined = false;
            m_isTopLevelWithQualifierInjected = false;
            m_parsedSpecs = new Dictionary<AbsolutePath, ISourceFile>();
            m_referencedModules = new HashSet<(ModuleDescriptor moduleDescriptor, Location location)>();
        }

        /// <summary>
        /// Notifies that one spec of this module failed at parsing.
        /// </summary>
        /// <remarks>
        /// It doesn't matter which one. This is used for recognizing when a module under construction is complete.
        /// </remarks>
        /// TODO: we could consider associating spec parsing failures to the module as well (instead of, or on top of, adding it
        /// at the workspace level)
        public void ParsingFailed()
        {
            Interlocked.Increment(ref m_failedSpecsCount);
        }

        /// <summary>
        /// A module is complete when all its specs are either parsed or declared to failed parsing.
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        public bool IsModuleComplete()
        {
            return Volatile.Read(ref m_parsedSpecsCount) + Volatile.Read(ref m_failedSpecsCount) == Definition.Specs.Count;
        }

        /// <summary>
        /// Returns true IFF this module is complete (<see cref="IsModuleComplete"/>) and this is the first time
        /// this method is called when <see cref="IsModuleComplete"/> is true.
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        internal bool IsModuleFirstTimeComplete()
        {
            return IsModuleComplete() && Interlocked.Increment(ref m_isModuleFirstTimeCompleteCounter) == 1;
        }

        /// <summary>
        /// Returns the paths of all the specs that are pending to be added
        /// </summary>
        public IReadOnlySet<(AbsolutePath path, string pathString)> GetPendingSpecPathsOrderedByPath(PathTable pathTable)
        {
            lock (m_parsedSpecs)
            {
                return
                    Definition.Specs
                        .Select(path => (path,pathString: path.ToString(pathTable)))
                        .Where(tpl => !m_parsedSpecs.Keys.Contains(tpl.path))
                        .OrderBy(tpl => tpl.pathString)
                        .ToReadOnlySet();
            }
        }

        /// <summary>
        /// Represents result of adding file to a module under construction.
        /// </summary>
        public enum AddParsedSpecResult
        {
            /// <summary>
            /// Regular file was added to the module.
            /// </summary>
            None,

            /// <summary>
            /// Special file was added to the queue that would be potentially modified at the <see cref="ModuleUnderConstruction.TryCreateCompletedModule"/> method
            /// by injecting some DScript specific nodes.
            /// </summary>
            SpecIsCandidateForInjectingQualifiers = 1,
        }

        /// <summary>
        /// Returns a special source file that could be potentially modified for DScript specific purposes.
        /// </summary>
        [CanBeNull]
        public ISourceFile GetSourceFileForInjectingQualifiers()
        {
            return m_parsedSpecs.Values.FirstOrDefault();
        }

        /// <summary>
        /// Adds a parsed spec to the module under construction.
        /// </summary>
        public AddParsedSpecResult AddParsedSpec(AbsolutePath path, ISourceFile spec)
        {
            var result = AddParsedSpecResult.None;
            lock (m_parsedSpecs)
            {
                if (m_parsedSpecs.Count == 0)
                {
                    result = AddParsedSpecResult.SpecIsCandidateForInjectingQualifiers;
                }

                // ModuleDefinition doesn't have duplicates, because Specs property is a set.
                m_parsedSpecs[path] = spec;
                m_isRootQualifierDefined |= spec.DeclaresRootQualifier;
                m_isTopLevelWithQualifierInjected |= spec.DeclaresInjectedTopLevelWithQualifier;
            }

            // We increment the number of parsed specs after adding it to the collection, so no other thread will think the module is complete
            Interlocked.Increment(ref m_parsedSpecsCount);
            return result;
        }

        /// <summary>
        /// Advertises that a module was referenced from this module under construction
        /// </summary>
        public void AddReferencedModule(ModuleDescriptor moduleDescriptor, Location provenance)
        {
            lock (m_referencedModules)
            {
                m_referencedModules.Add((moduleDescriptor, provenance));
            }
        }

        /// <summary>
        /// Creates a <see cref="ParsedModule"/> from this module under construction
        /// </summary>
        /// <remarks>
        /// All source files that are part of the newly created module are updated to contain
        /// all internal module references. External ones were already updated during parsing.
        /// Thread safe. It always returns the same instance of a parsed module for each instance of module under construction.
        /// If cancelOnFailure is set false, this method always succeeds
        /// </remarks>
        [System.Diagnostics.Contracts.Pure]
        public bool TryCreateCompletedModule(IModuleReferenceResolver referenceResolver, WorkspaceConfiguration workspaceConfiguration, bool cancelOnFailure, out ParsedModule module, out Failure[] failures)
        {
            Contract.Requires(IsModuleComplete());

            // If we already created the module, we just return it
            lock (m_parsedSpecs)
            {
                if (m_isCompleteModuleCreated)
                {
                    module = m_completedModule;
                    failures = null;
                    return module != null;
                }

                m_isCompleteModuleCreated = true;

                // The chances that m_referencedModules is updated here from another thread are slim, but still possible.
                // The module can be complete while other threads may have remaining specs under the process of parsing that were
                // already parsed and added to the module.
                lock (m_referencedModules)
                {
                    m_completedModule = new ParsedModule(Definition, m_parsedSpecs, m_referencedModules.ToReadOnlySet(), hasFailures: m_failedSpecsCount != 0);
                }

                if (!TryUpdateAllInternalModuleReferences(referenceResolver, workspaceConfiguration, out failures) && cancelOnFailure)
                {
                    module = null;
                    return false;
                }

                CompleteQualifierRelatedModuleInvariants();

                module = m_completedModule;

                return true;
            }
        }

        private bool TryUpdateAllInternalModuleReferences(IModuleReferenceResolver referenceResolver, WorkspaceConfiguration workspaceConfiguration, out Failure[] failures)
        {
            // Run update procedure in parallel if the threshold is met.
            // In some cases the module itself can be very big (25K specs and more).
            // And in some cases there is just one big module.
            // In this case, running the following procedure in parallel makes a lot of sense in terms of performance.
            Failure[] resultingFailures = null;
            if (m_completedModule.Specs.Count > workspaceConfiguration.ThreasholdForParallelUpdate)
            {
                Parallel.ForEach(
                    m_completedModule.Specs.Values,
                    new ParallelOptions() { MaxDegreeOfParallelism = workspaceConfiguration.MaxDegreeOfParallelismForParsing },
                    (spec, state) =>
                    {
                        if (!referenceResolver.TryUpdateAllInternalModuleReferences(spec, Definition, out Failure[] iterationFailures))
                        {
                            Volatile.Write(ref resultingFailures, iterationFailures);
                            state.Break();
                        }
                    });
            }
            else
            {
                foreach (var spec in m_completedModule.Specs.Values)
                {
                    if (!referenceResolver.TryUpdateAllInternalModuleReferences(spec, Definition, out resultingFailures))
                    {
                        break;
                    }
                }
            }

            failures = resultingFailures;
            return failures == null || failures.Length == 0;
        }

        /// <summary>
        /// There is qualifier-related invariants that need to hold for a V2 module:
        /// - A default top-level qualifier value needs to be injected if none of the module specs declare one
        /// - A top-level 'withQualifier' function needs to be injected
        /// </summary>
        private void CompleteQualifierRelatedModuleInvariants()
        {
            var targetSpec = GetSourceFileForInjectingQualifiers();

            // If none of the parsed specs define a root qualifier and the module is a V2 one, we generate a default qualifier
            // We pick a random spec and add it there, implicit visibility does the trick
            if (Definition.ResolutionSemantics == NameResolutionSemantics.ImplicitProjectReferences && targetSpec != null)
            {
                if (!m_isRootQualifierDefined)
                {
                    targetSpec.AddDefaultQualifierDeclaration();
                }

                // This happens once per module creation, but this flags handle the case where the IDE request a creation in incremental mode
                // so specs might have already gone through this
                if (!m_isTopLevelWithQualifierInjected)
                {
                    // 'withQualifier' was already added for every namespace (in the case of a V2 module). But we are still missing
                    // a 'withQualifier' at the top level. Again, we pick a random spec and add it there
                    targetSpec.AddTopLevelWithQualifierFunction(Definition.Descriptor.Name);
                }
            }
        }
    }
}

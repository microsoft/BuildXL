// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.ProcessPipExecutor;
using BuildXL.Processes.Sideband;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Contains global state necessary for pip execution
    /// </summary>
    public partial class PipExecutionState
    {
        /// <summary>
        /// Gets the pip scoped execution state
        /// </summary>
        public PipScopeState GetScope(Process pip)
        {
            bool ifPreserveOutputs = pip.AllowPreserveOutputs && m_unsafeConfiguration.PreserveOutputsTrustLevel <= pip.PreserveOutputsTrustLevel;
            return pip.Provenance != null ? new PipScopeState(this, pip.Provenance.ModuleId, ifPreserveOutputs) : new PipScopeState(this, ModuleId.Invalid, ifPreserveOutputs);
        }

        /// <summary>
        /// The scoped execution state. Ensures that state is only accessed in a scoped context.
        /// </summary>
        public sealed class PipScopeState
        {
            /// <summary>
            /// File-access operations that should be allowed (without even a warning) but will not be predicted.
            /// </summary>
            public FileAccessAllowlist FileAccessAllowlist { get; private set; }

            /// <summary>
            /// Rules for computing directory fingerprints
            /// </summary>
            public DirectoryMembershipFingerprinterRuleSet DirectoryMembershipFingerprinterRuleSet { get; private set; }

            /// <summary>
            /// Used to retrieve semantic path information
            /// </summary>
            public SemanticPathExpander PathExpander { get; private set; }

            /// <summary>
            /// Unsafe options that go into the pip's pathset
            /// </summary>
            public UnsafeOptions UnsafeOptions { get; private set; }

            /// <summary>
            /// <see cref="PipExecutionState.AlienFileEnumerationCache"/>
            /// </summary>
            public ConcurrentBigMap<AbsolutePath, IReadOnlyList<(AbsolutePath, string)>> AlienFileEnumerationCache { get; private set; }

            /// <summary>
            /// <see cref="PipExecutionState.SidebandState"/>
            /// </summary>
            public SidebandState SidebandState { get; }

            /// <summary>
            /// The cacheable pip abstraction which defines cache relevant data and behavior
            /// </summary>
            private CacheableProcess m_cacheablePip;

            /// <summary>
            /// Cache of "relevant untracked paths, grouped by parent directory" for the pip owned by this scope state.
            /// See <see cref="GetOrBuildUntrackedPathsByParent"/>.
            /// </summary>
            private Dictionary<AbsolutePath, HashSet<AbsolutePath>> m_untrackedPathsByParent;

            /// <summary>
            /// Class constructor. Do not call from outside parent class.
            /// </summary>
            /// <param name="parent">the parent execution state containing the global information from which module specific instances can be retrieved</param>
            /// <param name="moduleId">the module id of the state to create</param>
            /// <param name="ifPreserveOutputs">whether preserveOutputs is allowed for the pip and pip's trust level is bigger than the unsafeConfiguraion.PreserveoutputTrustLevle</param>
            internal PipScopeState(PipExecutionState parent, ModuleId moduleId, bool ifPreserveOutputs)
            {
                if (moduleId.IsValid)
                {
                    FileAccessAllowlist = parent.m_fileAccessAllowlist?.GetModuleAllowlist(moduleId);
                    Contract.Assume(parent.m_pathExpander != null, "m_pathExpander cannot be null. This is envorced by PipExecutionState's constructor");
                    PathExpander = parent.m_pathExpander.GetModuleExpander(moduleId);
                    DirectoryMembershipFingerprinterRuleSet = parent.m_directoryMembershipFingerprinterRuleSet?.GetModuleRule(moduleId);
                }
                else
                {
                    FileAccessAllowlist = parent.m_fileAccessAllowlist;
                    PathExpander = parent.m_pathExpander;
                    DirectoryMembershipFingerprinterRuleSet = parent.m_directoryMembershipFingerprinterRuleSet;
                }

                AlienFileEnumerationCache = parent.AlienFileEnumerationCache;
                SidebandState = parent.SidebandState;
                UnsafeOptions = new UnsafeOptions(parent.m_unsafeConfiguration, ifPreserveOutputs ? parent.m_preserveOutputsSalt : UnsafeOptions.PreserveOutputsNotUsed);
            }

            /// <summary>
            /// Gets the cacheable pip abstraction for the process. This data is cached so that subsequent
            /// calls do not create a new cacheable pip.
            /// </summary>
            public CacheableProcess GetCacheableProcess(Process process, IPipExecutionEnvironment environment)
            {
                m_cacheablePip = m_cacheablePip ?? new CacheableProcess(process, environment);
                return m_cacheablePip;
            }

            /// <summary>
            /// Returns a map from a directory to the set of "relevant untracked paths" that are direct children
            /// of that directory. The three source collections (<see cref="Process.UntrackedScopes"/>,
            /// <see cref="Process.UntrackedPaths"/>, and <see cref="IPipExecutionEnvironment.TranslatedGlobalUnsafeUntrackedScopes"/>)
            /// are invariant for a pip's cache lookup, but <see cref="ObservedInputProcessor"/> queries the fingerprint
            /// of the same directory tens of thousands of times per pip. Grouping once by parent directory turns the
            /// per-call scan into an O(1) dictionary lookup. Cached on this scope state so that the grouping is reused
            /// across all pathset processing calls for the same pip within one cache lookup.
            /// </summary>
            public Dictionary<AbsolutePath, HashSet<AbsolutePath>> GetOrBuildUntrackedPathsByParent(IPipExecutionEnvironment environment)
            {
                Contract.Requires(m_cacheablePip != null, "GetCacheableProcess must be called before GetOrBuildUntrackedPathsByParent");

                if (m_untrackedPathsByParent != null)
                {
                    return m_untrackedPathsByParent;
                }

                var process = m_cacheablePip.Process;
                var pathTable = environment.Context.PathTable;
                var byParent = new Dictionary<AbsolutePath, HashSet<AbsolutePath>>();

                addPathsGroupedByParent(process.UntrackedScopes);
                addPathsGroupedByParent(process.UntrackedPaths);
                addPathsGroupedByParent(environment.TranslatedGlobalUnsafeUntrackedScopes);

                m_untrackedPathsByParent = byParent;
                return byParent;

                void addPathsGroupedByParent(IEnumerable<AbsolutePath> paths)
                {
                    foreach (var path in paths)
                    {
                        var parent = path.GetParent(pathTable);
                        if (!parent.IsValid)
                        {
                            continue;
                        }

                        if (!byParent.TryGetValue(parent, out var group))
                        {
                            group = new HashSet<AbsolutePath>();
                            byParent[parent] = group;
                        }

                        group.Add(path);
                    }
                }
            }
        }
    }
}

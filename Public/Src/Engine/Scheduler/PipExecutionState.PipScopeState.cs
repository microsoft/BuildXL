// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;

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
            return pip.Provenance != null ? new PipScopeState(this, pip.Provenance.ModuleId, pip.AllowPreserveOutputs) : new PipScopeState(this, ModuleId.Invalid, pip.AllowPreserveOutputs);
        }

        /// <summary>
        /// The scoped execution state. Ensures that state is only accessed in a scoped context.
        /// </summary>
        public sealed class PipScopeState
        {
            /// <summary>
            /// File-access operations that should be allowed (without even a warning) but will not be predicted.
            /// </summary>
            public FileAccessWhitelist FileAccessWhitelist { get; private set; }

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
            /// The cacheable pip abstraction which defines cache relevant data and behavior
            /// </summary>
            private CacheableProcess m_cacheablePip;

            /// <summary>
            /// Class constructor. Do not call from outside parent class.
            /// </summary>
            /// <param name="parent">the parent execution state containing the global information from which module specific instances can be retrieved</param>
            /// <param name="moduleId">the module id of the state to create</param>
            /// <param name="allowPreserveOutputs">whether preserveOutputs is allowed for the pip</param>
            internal PipScopeState(PipExecutionState parent, ModuleId moduleId, bool allowPreserveOutputs)
            {
                if (moduleId.IsValid)
                {
                    FileAccessWhitelist = parent.m_fileAccessWhitelist?.GetModuleWhitelist(moduleId);
                    Contract.Assume(parent.m_pathExpander != null, "m_pathExpander cannot be null. This is envorced by PipExecutionState's constructor");
                    PathExpander = parent.m_pathExpander.GetModuleExpander(moduleId);
                    DirectoryMembershipFingerprinterRuleSet = parent.m_directoryMembershipFingerprinterRuleSet?.GetModuleRule(moduleId);
                }
                else
                {
                    FileAccessWhitelist = parent.m_fileAccessWhitelist;
                    PathExpander = parent.m_pathExpander;
                    DirectoryMembershipFingerprinterRuleSet = parent.m_directoryMembershipFingerprinterRuleSet;
                }

                UnsafeOptions = new UnsafeOptions(parent.m_unsafeConfiguration, allowPreserveOutputs ? parent.m_preserveOutputsSalt : UnsafeOptions.PreserveOutputsNotUsed);
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
        }
    }
}

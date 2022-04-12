// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Pips.Operations
{
    /// <nodoc />
    public partial class Process
    {
        /// <summary>
        /// Process-level default value for <see cref="ICacheConfiguration.AugmentWeakFingerprintRequiredPathCommonalityFactor"/> 
        /// if fingerprint augmentation is not explicitly set globally and 
        /// <see cref="Options.EnforceWeakFingerprintAugmentation"/> is on
        /// </summary>
        public static double DefaultAugmentWeakFingerprintRequiredPathCommonalityFactor = .4;

        /// <summary>
        /// Process-level default value for <see cref="ICacheConfiguration.AugmentWeakFingerprintPathSetThreshold"/> 
        /// if fingerprint augmentation is not explicitly set globally and 
        /// <see cref="Options.EnforceWeakFingerprintAugmentation"/> is on
        /// </summary>
        public static int DefaultAugmentWeakFingerprintPathSetThreshold = 5;

        /// <summary>
        /// Indicate whether the pip is used for integration test purposes.
        /// </summary>
        public const int IntegrationTestPriority = 99;

        /// <summary>
        /// Flag options controlling process pip behavior.
        /// </summary>
        [Flags]
        public enum Options : int
        {
            /// <nodoc />
            None = 0,

            /// <summary>
            /// If set, child processes started by this process are excluded from file access monitoring.
            /// </summary>
            HasUntrackedChildProcesses = 1 << 0,

            /// <summary>
            /// If set, the outputs of this process do not encode a dependency on absolute paths (the absolute paths
            /// of input dependencies can be trimmed based on declared mount points).
            /// </summary>
            ProducesPathIndependentOutputs = 1 << 1,

            /// <summary>
            /// If set, the outputs of this process must be left writable (the build engine may not defensively make them readonly)
            /// This prevents hardlinking of these outputs into the build cache, even if otherwise enabled.
            /// </summary>
            OutputsMustRemainWritable = 1 << 2,

            /// <summary>
            /// If set, allows output from a prior process execution to be left when running the process. This may be
            /// enabled to leverage incremental behavior of the pip. It should only be turned on when the determinism of
            /// the process is trusted
            /// </summary>
            AllowPreserveOutputs = 1 << 3,

            /// <summary>
            /// Light processes go to the light queue.
            /// </summary>
            IsLight = 1 << 4,

            /// <summary>
            /// Whether this process should run in an isolated container
            /// </summary>
            NeedsToRunInContainer = 1 << 5,

            /// <summary>
            /// Whether this process is allowed to read an undeclared source file.
            /// </summary>
            /// <remarks>
            /// A source file is considered to be a file that is not written during a build
            /// </remarks>
            AllowUndeclaredSourceReads = 1 << 6,

            /// <summary>
            /// Whether this process is configured to always be a cache miss.
            /// </summary>
            /// <remarks>
            /// When specified, no cache lookup will be performed for the pip.
            /// </remarks>
            DisableCacheLookup = 1 << 7,

            /// <summary>
            /// Whether this process is dependent on common OS binaries.
            /// </summary>
            /// <remarks>
            /// When specified, no cache lookup will be performed for the pip.
            /// </remarks>
            DependsOnCurrentOs = 1 << 8,

            /// <summary>
            /// Whether this process is dependent on windows' AppData folders
            /// </summary>
            /// <remarks>
            /// Windows only, has no effect on other operating systems.
            /// </remarks>
            DependsOnWindowsAppData = 1 << 9,

            /// <summary>
            /// Whether this process is dependent on windows' ProgramData location.
            /// </summary>
            /// <remarks>
            /// Windows only, has no effect on other operating systems.
            /// </remarks>
            DependsOnWindowsProgramData = 1 << 10,

            /// <summary>
            /// Whether this process requires admin privilege.
            /// </summary>
            RequiresAdmin = 1 << 11,

            /// <summary>
            /// Whether this process using non-empty <see cref="Process.PreserveOutputAllowlist"/>
            /// </summary>
            HasPreserveOutputAllowlist = 1 << 12,

            /// <summary>
            /// Incremental tool is superset of <see cref="AllowPreserveOutputs"/> and is only active when preserve output is active.
            /// </summary>
            IncrementalTool = (1 << 13) | AllowPreserveOutputs,

            /// <summary>
            /// Whether this process require unsafe_GlobalPassthroughEnvVars and unsafe_GlobalUntrackedScopes
            /// </summary>
            RequireGlobalDependencies = 1 << 14,

            /// <summary>
            /// If the global <see cref="ICacheConfiguration.AugmentWeakFingerprintPathSetThreshold"/> is not 
            /// set (i.e is equal to zero), this option forces fingerprint augmentation
            /// for this particular process using defaults specified by <see cref="DefaultAugmentWeakFingerprintPathSetThreshold"/> 
            /// and <see cref="DefaultAugmentWeakFingerprintRequiredPathCommonalityFactor"/>
            /// </summary>
            EnforceWeakFingerprintAugmentation = 1 << 15,

            /// <summary>
            /// This option makes all statically declared artifacts on this process (inputs and outputs) to be automatically
            /// added to the sandbox access report, as if the process actually produced those accesses
            /// </summary>
            /// <remarks>
            /// Useful for automatically augmenting the sandbox access report on trusted process breakaway. <see cref="ChildProcessesToBreakawayFromSandbox"/>.
            /// Default is false. This is an unsafe option. Should only be used on trusted process, where the statically declared inputs and outputs are guaranteed to
            /// match the process actual behavior.
            /// Only takes effect if <see cref="ChildProcessesToBreakawayFromSandbox"/> is a non-empty array. Otherwise is ignored.
            /// Note that when using this, the observed set of inputs can be larger than usual since observations cannot be used to determine what actually was read, and all
            /// statically specified inputs are used instead.
            /// </remarks>
            TrustStaticallyDeclaredAccesses = 1 << 16,

            /// <summary>
            /// When this option is set, the scheduler will not be able to cancel the specified pip for perforance purposes.
            /// </summary>
            Uncancellable = 1 << 17,

            /// <summary>
            /// When set, the serialized path set of this process is not normalized wrt casing
            /// </summary>
            /// <remarks>
            /// This is already the behavior when running in a non-Windows OS, therefore this option only has a effect on Windows systems.
            /// Setting this option increases the chance BuildXL will preserve path casing on Windows, at the cost of less efficient
            /// caching, where the same weak fingerprint may have different path sets that only differ in casing.
            /// </remarks>
            PreservePathSetCasing = 1 << 18,

            /// <summary>
            /// When set, the pip is considered to have failed executing if it writes to standard error, regardless of the pip exit code.
            /// </summary>
            WritingToStandardErrorFailsExecution = 1 << 19,

            /// <summary>
            /// Whether full reparse point resolving is disabled
            /// </summary>
            DisableFullReparsePointResolving = 1 << 20,

            /// <summary>
            /// Whether to disable sandboxing for this process
            /// </summary>
            DisableSandboxing = 1 << 21,
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Processes.Sideband;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Cache;
using BuildXL.Scheduler.FileSystem;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Defines state necessary for pip execution
    /// </summary>
    public sealed partial class PipExecutionState
    {
        // State below indicates state that is only available when scoped to a pip. These are the root values
        // for which the per-module instantiations can be retrieved.
        #region Scoped State

        private readonly IRootModuleConfiguration m_rootModuleConfiguration;

        /// <summary>
        /// RuleSet for DirectoryMembershipFingerprinter
        /// </summary>
        private readonly DirectoryMembershipFingerprinterRuleSet m_directoryMembershipFingerprinterRuleSet;

        /// <summary>
        /// Allowlist for allowed-without-warning-but-unpredicted file operations.
        /// </summary>
        private readonly FileAccessAllowlist m_fileAccessAllowlist;

        /// <summary>
        /// Used to retrieve semantic path information
        /// </summary>
        private readonly SemanticPathExpander m_pathExpander;

        /// <summary>
        /// The overall unsafe configuration
        /// </summary>
        private IUnsafeSandboxConfiguration m_unsafeConfiguration { get; }

        /// <summary>
        /// The global preserve outputs salt used for the build
        /// </summary>
        private PreserveOutputsInfo m_preserveOutputsSalt { get; }

        #endregion Scoped State

        /// <summary>
        /// The execution log
        /// </summary>
        public IExecutionLogTarget ExecutionLog { get; internal set; }

        /// <summary>
        /// The resource manager which tracks availability of resources for execution pips and enables cancellation of pips to free resources
        /// </summary>
        public ProcessResourceManager ResourceManager { get; }

        /// <summary>
        /// BuildXL cache layer. This provides several facets:
        /// - Cache of artifact content, used to address artifact content by hash.
        /// - Cache of prior pip executions
        /// </summary>
        public PipTwoPhaseCache Cache { get; }

        /// <summary>
        /// The file content manager used for tracking, hashing, materializing files
        /// Setter is internal for unit tests only.
        /// </summary>
        public FileContentManager FileContentManager { get; internal set; }

        /// <summary>
        /// Directory membership fingerprinter.
        /// </summary>
        public IDirectoryMembershipFingerprinter DirectoryMembershipFingerprinter { get; private set; }

        /// <summary>
        /// Gets the service manager for launching services necessary to execute pips
        /// </summary>
        public ServiceManager ServiceManager { get; }

        /// <summary>
        /// The file system view which tracks existence of files/directories in real/graph filesystems
        /// </summary>
        public FileSystemView FileSystemView { get; internal set; }

        /// <summary>
        /// Gets the snapshot of the environment variables
        /// </summary>
        public PipEnvironment PipEnvironment { get; }

        /// <summary>
        /// Whether lazy deletion of shared opaque outputs is enabled;
        /// </summary>
        public bool LazyDeletionOfSharedOpaqueOutputsEnabled => SidebandState?.ShouldPostponeDeletion == true;

        /// <summary>
        /// Cache used to hold alien file enumerations per directory
        /// </summary>
        public ConcurrentBigMap<AbsolutePath, IReadOnlyList<(AbsolutePath, string)>> AlienFileEnumerationCache { get; }
        
        /// <summary>
        /// Helper to identify files that were present before the build started
        /// </summary>
        public FileTimestampTracker FileTimestampTracker { get; }

        public SidebandState SidebandState { get; }

        /// <summary>
        /// Class constructor
        /// </summary>
        public PipExecutionState(
            IConfiguration configuration,
            LoggingContext loggingContext,
            PipTwoPhaseCache cache,
            FileAccessAllowlist fileAccessAllowlist,
            IDirectoryMembershipFingerprinter directoryMembershipFingerprinter,
            SemanticPathExpander pathExpander,
            IExecutionLogTarget executionLog,
            DirectoryMembershipFingerprinterRuleSet directoryMembershipFinterprinterRuleSet,
            FileContentManager fileContentManager,
            IUnsafeSandboxConfiguration unsafeConfiguration,
            PreserveOutputsInfo preserveOutputsSalt,
            FileSystemView fileSystemView,
            ConcurrentBigMap<AbsolutePath, IReadOnlyList<(AbsolutePath, string)>> alienFileEnumerationCache,
            FileTimestampTracker fileTimestampTracker,
            ServiceManager serviceManager = null,
            SidebandState sidebandState = null)
        {
            Contract.Requires(fileContentManager != null);
            Contract.Requires(directoryMembershipFingerprinter != null);
            Contract.Requires(pathExpander != null);
            Contract.RequiresNotNull(alienFileEnumerationCache);
            Contract.RequiresNotNull(fileTimestampTracker);

            Cache = cache;
            m_fileAccessAllowlist = fileAccessAllowlist;
            DirectoryMembershipFingerprinter = directoryMembershipFingerprinter;
            ResourceManager = new ProcessResourceManager(loggingContext);
            m_pathExpander = new FileContentManagerSemanticPathExpander(fileContentManager, pathExpander);
            ExecutionLog = executionLog;
            m_rootModuleConfiguration = configuration;
            m_directoryMembershipFingerprinterRuleSet = directoryMembershipFinterprinterRuleSet;
            FileContentManager = fileContentManager;
            ServiceManager = serviceManager ?? ServiceManager.Default;
            PipEnvironment = new PipEnvironment(loggingContext);
            FileSystemView = fileSystemView;
            m_unsafeConfiguration = unsafeConfiguration;
            m_preserveOutputsSalt = preserveOutputsSalt;
            AlienFileEnumerationCache = alienFileEnumerationCache;
            SidebandState = sidebandState;
            FileTimestampTracker = fileTimestampTracker;

            if (fileSystemView != null)
            {
                fileContentManager.SetLocalDiskFileSystemExistenceView(fileSystemView);
            }
        }
    }
}

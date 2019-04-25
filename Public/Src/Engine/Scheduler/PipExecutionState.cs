// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Cache;
using BuildXL.Scheduler.FileSystem;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

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
        /// Whitelist for allowed-without-warning-but-unpredicted file operations.
        /// </summary>
        private readonly FileAccessWhitelist m_fileAccessWhitelist;

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
        private ContentHash m_preserveOutputsSalt { get; }

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
        /// Directory mempership fingerprinter.
        /// </summary>
        public IDirectoryMembershipFingerprinter DirectoryMembershipFingerprinter { get; private set; }

        /// <summary>
        /// Cache for checking path existence.
        /// </summary>
        public ConcurrentBigMap<AbsolutePath, PathExistence> PathExistenceCache { get; private set; }

        /// <summary>
        /// Gets the service manager for launching services necessary to execute pips
        /// </summary>
        public ServiceManager ServiceManager { get; }

        /// <summary>
        /// The file system view which tracks existence of files/directories in real/graph filesystems
        /// </summary>
        public FileSystemView FileSystemView { get; }

        /// <summary>
        /// Gets the snapshot of the environment variables
        /// </summary>
        public PipEnvironment PipEnvironment { get; }

        /// <summary>
        /// Class constructor
        /// </summary>
        public PipExecutionState(
            IRootModuleConfiguration rootModuleConfiguration,
            PipTwoPhaseCache cache,
            FileAccessWhitelist fileAccessWhitelist,
            IDirectoryMembershipFingerprinter directoryMembershipFingerprinter,
            SemanticPathExpander pathExpander,
            IExecutionLogTarget executionLog,
            DirectoryMembershipFingerprinterRuleSet directoryMembershipFinterprinterRuleSet,
            FileContentManager fileContentManager,
            IUnsafeSandboxConfiguration unsafeConfiguration,
            ContentHash preserveOutputsSalt,
            FileSystemView fileSystemView,
            ServiceManager serviceManager = null)
        {
            Contract.Requires(fileContentManager != null);
            Contract.Requires(directoryMembershipFingerprinter != null);
            Contract.Requires(pathExpander != null);

            Cache = cache;
            m_fileAccessWhitelist = fileAccessWhitelist;
            DirectoryMembershipFingerprinter = directoryMembershipFingerprinter;
            ResourceManager = new ProcessResourceManager();
            m_pathExpander = new FileContentManagerSemanticPathExpander(fileContentManager, pathExpander);
            ExecutionLog = executionLog;
            m_rootModuleConfiguration = rootModuleConfiguration;
            m_directoryMembershipFingerprinterRuleSet = directoryMembershipFinterprinterRuleSet;
            PathExistenceCache = new ConcurrentBigMap<AbsolutePath, PathExistence>();
            FileContentManager = fileContentManager;
            ServiceManager = serviceManager ?? ServiceManager.Default;
            PipEnvironment = new PipEnvironment();
            FileSystemView = fileSystemView;
            m_unsafeConfiguration = unsafeConfiguration;
            m_preserveOutputsSalt = preserveOutputsSalt;

            if (fileSystemView != null)
            {
                fileContentManager.SetLocalDiskFileSystemExistenceView(fileSystemView);
            }
        }
    }
}

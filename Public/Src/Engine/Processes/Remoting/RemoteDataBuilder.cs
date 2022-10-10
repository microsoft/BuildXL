// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Builds an instance of <see cref="RemoteData"/>
    /// </summary>
    internal class RemoteDataBuilder
    {
        private readonly HashSet<AbsolutePath> m_fileDependencies = new();
        private readonly HashSet<AbsolutePath> m_directoryDependencies = new();
        private readonly HashSet<AbsolutePath> m_outputDirectories = new();
        private readonly HashSet<AbsolutePath> m_untrackedScopes = new();
        private readonly HashSet<AbsolutePath> m_untrackedPaths = new();
        private readonly HashSet<AbsolutePath> m_tempDirectories = new();
        private SandboxedProcessInfo? m_processInfo;
        private readonly IRemoteProcessManager m_remoteProcessManager;
        private readonly Process m_process;
        private readonly PathTable m_pathTable;

        /// <summary>
        /// Constructor.
        /// </summary>
        public RemoteDataBuilder(IRemoteProcessManager remoteProcessManager, Process process, PathTable pathTable)
        {
            m_remoteProcessManager = remoteProcessManager;
            m_process = process;
            m_pathTable = pathTable;
        }

        /// <summary>
        /// Adds an untracked scope.
        /// </summary>
        public void AddUntrackedScope(AbsolutePath path) => m_untrackedScopes.Add(path);

        /// <summary>
        /// Adds an untracked path.
        /// </summary>
        public void AddUntrackedPath(AbsolutePath path) => m_untrackedPaths.Add(path);

        /// <summary>
        /// Adds a temp directory.
        /// </summary>
        public void AddTempDirectory(AbsolutePath path) => m_tempDirectories.Add(path);

        /// <summary>
        /// Adds a file dependency.
        /// </summary>
        public void AddFileDependency(AbsolutePath path) => m_fileDependencies.Add(path);

        /// <summary>
        /// Adds a directory dependency.
        /// </summary>
        public void AddDirectoryDependency(AbsolutePath path) => m_directoryDependencies.Add(path);

        /// <summary>
        /// Adds an output directory.
        /// </summary>
        /// <param name="path"></param>
        public void AddOutputDirectory(AbsolutePath path) => m_outputDirectories.Add(path);

        /// <summary>
        /// Sets process info.
        /// </summary>
        public void SetProcessInfo(SandboxedProcessInfo processInfo) => m_processInfo = processInfo;

        /// <summary>
        /// Builds an instance of <see cref="RemoteData"/>.
        /// </summary>
        public async Task<RemoteData> BuildAsync()
        {
            Contract.Requires(m_processInfo != null);

            m_fileDependencies.UnionWith(await m_remoteProcessManager.GetInputPredictionAsync(m_process));

            var remoteData = new RemoteData
            {
                Executable = m_processInfo!.FileName,
                Arguments = m_processInfo!.Arguments,
                WorkingDirectory = m_processInfo!.WorkingDirectory,
            };

            if (m_processInfo!.EnvironmentVariables != null)
            {
                remoteData.EnvironmentVariables.AddRange(m_processInfo!.EnvironmentVariables.ToDictionary());
            }

            remoteData.FileDependencies.AddRange(ToStringPaths(m_fileDependencies));
            remoteData.DirectoryDependencies.AddRange(ToStringPaths(m_directoryDependencies));
            remoteData.OutputDirectories.AddRange(ToStringPaths(m_outputDirectories));
            remoteData.TempDirectories.AddRange(ToStringPaths(m_tempDirectories));
            remoteData.UntrackedScopes.AddRange(ToStringPaths(m_untrackedScopes));
            remoteData.UntrackedPaths.AddRange(ToStringPaths(m_untrackedPaths));

            return remoteData;
        }

        private IEnumerable<string> ToStringPaths(IEnumerable<AbsolutePath> paths) => paths.Select(p => p.ToString(m_pathTable));
    }
}

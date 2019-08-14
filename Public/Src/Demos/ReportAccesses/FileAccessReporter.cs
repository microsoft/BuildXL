// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Demo
{
    /// <summary>
    /// A very simplistic use of BuildXL sandbox, just meant for observing file accesses
    /// </summary>
    public class FileAccessReporter : ISandboxedProcessFileStorage
    {
        private readonly LoggingContext m_loggingContext;

        /// <nodoc/>
        public PathTable PathTable { get; }

        /// <nodoc/>
        public FileAccessReporter()
        {
            PathTable = new PathTable();
            m_loggingContext = new LoggingContext(nameof(FileAccessReporter));
        }

        /// <summary>
        /// Runs the given tool with the provided arguments under the BuildXL sandbox and reports the result in a <see cref="SandboxedProcessResult"/>
        /// </summary>
        public Task<SandboxedProcessResult> RunProcessUnderSandbox(string pathToProcess, string arguments)
        {
            var info = new SandboxedProcessInfo(
                PathTable,
                this,
                pathToProcess,
                CreateManifestToAllowAllAccesses(PathTable),
                disableConHostSharing: true,
                containerConfiguration: ContainerConfiguration.DisabledIsolation,
                loggingContext: m_loggingContext)
            {
                Arguments = arguments,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                PipSemiStableHash = 0,
                PipDescription = "Simple sandbox demo",
                SandboxConnection = OperatingSystemHelper.IsUnixOS ? new SandboxConnectionKext() : null
            };

            var process = SandboxedProcessFactory.StartAsync(info, forceSandboxing: true).GetAwaiter().GetResult();

            return process.GetResultAsync();
        }

        /// <nodoc />
        string ISandboxedProcessFileStorage.GetFileName(SandboxedProcessFile file)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), file.DefaultFileName());
        }

        /// <summary>
        /// The manifest is configured so all file accesses are allowed but reported, including child processes.
        /// </summary>
        /// <remarks>
        /// Some special folders (Windows, InternetCache and History) are added as known scopes. Everything else will be flagged
        /// as an 'unexpected' access. However, unexpected accesses are configured so they are not blocked.
        /// </remarks>
        private static FileAccessManifest CreateManifestToAllowAllAccesses(PathTable pathTable)
        {
            var fileAccessManifest = new FileAccessManifest(pathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
            };

            fileAccessManifest.AddScope(
                AbsolutePath.Create(pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.Windows)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAll);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.InternetCache)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAll);

            fileAccessManifest.AddScope(
                AbsolutePath.Create(pathTable, SpecialFolderUtilities.GetFolderPath(Environment.SpecialFolder.History)),
                FileAccessPolicy.MaskAll,
                FileAccessPolicy.AllowAll);


            return fileAccessManifest;
        }
    }
}

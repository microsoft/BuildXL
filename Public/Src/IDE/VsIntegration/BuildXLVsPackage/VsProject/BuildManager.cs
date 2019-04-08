// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;
using Microsoft.Win32.SafeHandles;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.VsPackage.VsProject
{
    /// <summary>
    /// Manages performing the actual BuildXL build.
    ///
    /// Calls should be made in this order.
    ///
    /// 0. SetIdeFolderPath - called once before any StartBuild operations when the solution is loaded.
    /// 1. StartBuild
    /// 2. For each project, BuildProjectAsync(...) should be called to register the project for build
    ///      The last project will be the only one whose task does not immediately complete.
    /// 3. CancelBuild (optional) - this may be called if the build is canceled. Safe to call multiple times.
    /// 4. EndBuild - this is automatically called at the end of the build as well so its not explicitly necessary.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    public class BuildManager
    {
        private Process m_buildProcess;
        private Task<bool> m_currentBuild;
        private TaskCompletionSource<bool> m_buildInitiator;
        private TaskCompletionSource<int> m_processIdSource;

        private static readonly Regex s_processIdRegex = new Regex($"{Regex.Escape("http://localhost:9700/BuildXL/")}(?<processId>\\d+)");

        private CancellationTokenSource m_currentBuildCancellation;
        private string m_ideFolderPath;
        private string m_ideBuildCommandPath;
        private readonly object m_syncLock = new object();

        private ConcurrentDictionary<string, string> m_filtersByProject = new ConcurrentDictionary<string, string>();

        /// <nodoc />
        public bool BuildInProgress { get; set; }

        private IBuildManagerHost m_host;

        /// <nodoc />
        public BuildManager(IBuildManagerHost host)
        {
            m_host = host;
        }

        /// <nodoc />
        public void SetIdeFolderPath(string ideFolderPath)
        {
            m_ideFolderPath = ideFolderPath;
            m_ideBuildCommandPath = ideFolderPath != null ? Path.Combine(Path.GetFullPath(ideFolderPath), "build.cmd") : null;
        }

        /// <nodoc />
        public Task<bool> BuildProjectAsync(string projectName, string filter)
        {
            if (!BuildInProgress)
            {
                // m_host.WriteBuildMessage($"Error: No build in progress");
                // return Task.FromResult(false);

                // HasMoreProjects() rarely returns false for native projects even though there are projects to build.
                // That's why, we return true here to avoid the build failures.
                // TODO: First-class integration support is needed.
                return Task.FromResult(true);
            }

            if (string.IsNullOrEmpty(m_ideFolderPath))
            {
                m_host.WriteBuildMessage($"Error: No IDE folder path set. Is a solution open?");
                return Task.FromResult(false);
            }

            m_filtersByProject[projectName] = filter;

            if (m_host.HasMoreProjects())
            {
                // Allow the current build to just complete successfully if more projects will
                // build. The last project to build will propagate the actual build result.
                return Task.FromResult(true);
            }
            else
            {
                // This is the last project. Actually trigger the build now that all the projects are registered with the build.
                m_buildInitiator.TrySetResult(true);
            }

            return m_currentBuild;
        }

        private async Task<bool> StartBuildCoreAsync(BuildStartArguments buildStartArgs, CancellationToken cancellationToken)
        {
            try
            {
                // This task will be triggered once the last project is building
                await m_buildInitiator.Task;

                if (string.IsNullOrEmpty(m_ideFolderPath))
                {
                    m_host.WriteBuildMessage($"Error: No IDE folder path set. Is a solution open?");
                    return false;
                }

                var buildProcess = StartBuildProcess(buildStartArgs);
                if (buildProcess == null)
                {
                    return true;
                }

                m_buildProcess = buildProcess;
                cancellationToken.Register(() => CancelBuildProcess(buildProcess));
                var result = await WaitForProcessExitAsync(buildProcess, cancellationToken);

                return result;
            }
            catch (OperationCanceledException)
            {
                // Do nothing
                return false;
            }
            catch (Exception ex)
            {
                m_host.WriteBuildMessage($"Error: Starting build.\n{ex.ToString()}");
                return false;
            }
            finally
            {
                EndBuild();
            }
        }

        /// <nodoc />
        public void StartBuild(BuildStartArguments buildStartArguments)
        {
            lock (m_syncLock)
            {
                if (m_currentBuild == null)
                {
                    m_processIdSource = new TaskCompletionSource<int>();
                    m_buildInitiator = new TaskCompletionSource<bool>();
                    m_currentBuildCancellation = new CancellationTokenSource();
                    m_currentBuildCancellation.Token.Register(() => m_buildInitiator.TrySetCanceled());
                    m_currentBuild = StartBuildCoreAsync(buildStartArguments, m_currentBuildCancellation.Token);
                    BuildInProgress = true;
                }
            }
        }

        /// <nodoc />
        public void EndBuild()
        {
            m_filtersByProject.Clear();

            lock (m_syncLock)
            {
                m_buildProcess = null;
                m_currentBuild = null;
                BuildInProgress = false;
            }
        }

        private Process StartBuildProcess(BuildStartArguments buildStartArgs)
        {
            if (!File.Exists(m_ideBuildCommandPath))
            {
                m_host.WriteBuildMessage($"Warning:  Skipping build. Could not find build command file at: {m_ideBuildCommandPath}.");
                return null;
            }

            var commandArguments = string.Format(CultureInfo.InvariantCulture, "/C \"{0}\"", m_ideBuildCommandPath);
            m_host.WriteBuildMessage($"Running command: {commandArguments}");

            var specFilters = "/f:" + string.Join(" or ", m_filtersByProject.Values);

            File.WriteAllText($"{m_ideBuildCommandPath}.rsp", specFilters);

            var process = new Process();
            var processStartInfo = new ProcessStartInfo("cmd", commandArguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            process.StartInfo = processStartInfo;
            process.OutputDataReceived += (sender, args) => WriteBuildMessage(args.Data);
            process.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    m_host.WriteBuildMessage(args.Data);
                }
            };

            process.Start();

            // Start both the stream readers asynchronously
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        private async void CancelBuildProcess(Process buildCommandProcess)
        {
            try
            {
                // Wait for 5 seconds for the BuildXL process ID to be known by parsing console output so
                // BuildXL can be killed directly.
#pragma warning disable EPC13 // Suspiciously unobserved result.
                await Task.WhenAny(m_processIdSource.Task, Task.Delay(TimeSpan.FromSeconds(5)));
#pragma warning restore EPC13 // Suspiciously unobserved result.
                if (m_processIdSource.Task.IsCompleted)
                {
                    var processId = await m_processIdSource.Task;
                    var process = Process.GetProcessById(processId);
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
            }

            try
            {
                // Kill the cmd process
                buildCommandProcess.Kill();
            }
            catch
            {
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        private void WriteBuildMessage(string data)
        {
            if (data == null)
            {
                return;
            }

            if (!m_processIdSource.Task.IsCompleted)
            {
                var match = s_processIdRegex.Match(data);
                if (match.Success)
                {
                    int processId = int.Parse(match.Groups["processId"].Value);
                    m_host.WriteBuildMessage("BUILDXL PROCESS ID: " + processId);
                    m_processIdSource.TrySetResult(processId);
                }
            }

            m_host.WriteBuildMessage(data);
        }

        private static async Task<bool> WaitForProcessExitAsync(Process process, CancellationToken cancellationToken)
        {
            // Helps to get notified either when the server is ready or in case server failed to initialize properly
            using (var safeWaitHandle = new SafeWaitHandle(process.Handle, false))
            {
                using (var processWaitHandle = new AutoResetEvent(false) { SafeWaitHandle = safeWaitHandle })
                {
                    var completedIndex = await TaskUtilities.ToTask(new WaitHandle[] { processWaitHandle, cancellationToken.WaitHandle });

                    if (completedIndex == 0)
                    {
                        await Task.Run(() => process.WaitForExit(), cancellationToken);

                        return process.ExitCode == 0;
                    }

                    return false;
                }
            }
        }

        /// <nodoc />
        public void WriteIncompatibleMessage(string name)
        {
            m_host.WriteBuildMessage($"\t{name} is not compatible with the installed BuildXL Integration plugin.");
        }

        /// <nodoc />
        public void CancelBuild()
        {
            lock (m_syncLock)
            {
                if (m_currentBuildCancellation != null)
                {
                    m_currentBuildCancellation.Cancel();
                    m_currentBuildCancellation.Dispose();
                    m_currentBuildCancellation = null;
                }
            }
        }
    }

    /// <nodoc />
    public class BuildStartArguments
    {
        /// <nodoc />
        public string Configuration { get; set; } = "Debug";

        /// <nodoc />
        public string Platform { get; set; } = "AnyCpu";
    }
}

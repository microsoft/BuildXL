// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Processes.Remoting;
using BuildXL.SandboxedProcessExecutor.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.VmCommandProxy;

#nullable enable

namespace BuildXL.SandboxedProcessExecutor
{
    internal sealed class Executor
    {
        private const int ProcessRelauchCountMax = 5;

        private readonly Configuration m_configuration;
        private readonly LoggingContext m_loggingContext = new ("BuildXL.SandboxedProcessExecutor");
        public readonly TrackingEventListener TrackingEventListener = new (Events.Log);
        private readonly Stopwatch m_telemetryStopwatch = new ();
        private OutputErrorObserver? m_outputErrorObserver;
        private readonly ConsoleLogger m_logger = new ();
        private ISandboxConnection? m_sandboxConnection = null;
        private const int ReportQueueSizeForKextMB = 1024;
        private readonly bool m_isRunningInCloudBuildVm = false;

        /// <summary>
        /// Creates an instance of <see cref="Executor"/>.
        /// </summary>
        public Executor(Configuration configuration)
        {
            Contract.Requires(configuration != null);

            m_configuration = configuration;

            if (Environment.GetEnvironmentVariable("CLOUDBUILD_VM") == "1")
            {
                m_isRunningInCloudBuildVm = true;
            }
        }

        public int Run()
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (sender, eventArgs) =>
                {
                    if (eventArgs.ExceptionObject is Exception e)
                    {
                        HandleUnhandledFailure(e);
                    }
                };

            TelemetryStartup();

            ExitCode exitCode = RunInternal();

            TelemetryShutdown();

            return (int)exitCode;
        }

        private void HandleUnhandledFailure(Exception exception)
        {
            // Show the exception to the user
            m_logger.LogError(exception.ToString());

            // Log the exception to telemetry
            if (AriaV2StaticState.IsEnabled)
            {
                Logger.Log.SandboxedProcessExecutorCatastrophicFailure(m_loggingContext, exception.ToString());
                TelemetryShutdown();
            }

            Environment.Exit((int)ExitCode.InternalError);
        }

        private void TelemetryStartup()
        {
            if (!Debugger.IsAttached && m_configuration.EnableTelemetry)
            {
                AriaV2StaticState.Enable(BuildXL.Tracing.AriaTenantToken.Key);
                TrackingEventListener.RegisterEventSource(ETWLogger.Log);
                m_telemetryStopwatch.Start();
            }
        }

        private void TelemetryShutdown()
        {
            if (AriaV2StaticState.IsEnabled && m_configuration.EnableTelemetry)
            {
                m_telemetryStopwatch.Stop();
                Logger.Log.SandboxedProcessExecutorInvoked(m_loggingContext, m_telemetryStopwatch.ElapsedMilliseconds, Environment.CommandLine);
                AriaV2StaticState.TryShutDown(TimeSpan.FromSeconds(10), out _);
            }
        }

        /// <summary>
        /// Pings <see cref="Configuration.SandboxedProcessInfoInputFile"/> file periodically to identify connectivity issues between Host and VM.
        /// </summary>
        /// <returns>True when a connection issue is detected.</returns>
        internal void DetectConnectionIssuesBetweenHostAndVm()
        {
            while (true)
            {
                try
                {
                    if (File.Exists(Path.GetFullPath(m_configuration.SandboxedProcessInfoInputFile)))
                    {
                        Thread.Sleep(TimeSpan.FromMilliseconds(200));
                        continue;
                    }
                    break;
                }
                catch (Exception e) // All exceptions indicate the same result (connectivity issue detected)
                {
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    Console.Error.WriteLine("Execution error during DetectInfrastructureIssuesBetweenHostAndVm: " + e);
                    break;
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                }
            }
        }

        internal ExitCode RunInternal()
        {
            if (!TryReadSandboxedProcessInfo(out SandboxedProcessInfo? sandboxedProcessInfo))
            {
                return ExitCode.FailedReadInput;
            }

            RemoteData? remoteSandboxedProcessData = null;

            if (!string.IsNullOrEmpty(m_configuration.RemoteSandboxedProcessDataFile)
                && !TryReadRemoteSandboxedProcessData(out remoteSandboxedProcessData))
            {
                return ExitCode.FailedReadInput;
            }

            SandboxedProcessExecutorTestHook? sandboxedProcessExecutorTestHook = null;

            if (!string.IsNullOrEmpty(m_configuration.SandboxedProcessExecutorTestHookFile)
                && !TryReadSandboxedProcessExecutorTestHook(out sandboxedProcessExecutorTestHook))
            {
                return ExitCode.FailedReadInput;
            }

            if (sandboxedProcessExecutorTestHook?.FailVmConnection == true)
            {
                return ExitCode.VmConnectionError;
            }

            Thread? pingHost = null;
            if (m_isRunningInCloudBuildVm)
            {
                pingHost = new Thread(DetectConnectionIssuesBetweenHostAndVm)
                {
                    IsBackground = true   // To avoid blocking after current thread completes
                };
                pingHost.Start();
            }

            if (!TryPrepareSandboxedProcess(sandboxedProcessInfo!, remoteSandboxedProcessData))
            {
                return ExitCode.FailedSandboxPreparation;
            }

            (ExitCode exitCode, SandboxedProcessResult? result) executeResult;
            using (sandboxedProcessInfo!.SidebandWriter)
            {
                executeResult = ExecuteAsync(sandboxedProcessInfo).GetAwaiter().GetResult();
                sandboxedProcessInfo.SidebandWriter?.EnsureHeaderWritten();
            }

            if (pingHost != null && !pingHost.IsAlive)
            {
                return ExitCode.VmConnectionError;
            }

            if (pingHost != null && pingHost.IsAlive)
            {
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
#pragma warning disable SYSLIB0006 // 'Thread.Abort()' is obsolete: 'Thread.Abort is not supported and throws PlatformNotSupportedException.'
                try { pingHost.Abort(); }
                catch (Exception) { }       // Ignoring Exceptions since Thread abort is not supported in .net core
#pragma warning restore SYSLIB0021 // 'Thread.Abort()' is obsolete: 'Thread.Abort is not supported and throws PlatformNotSupportedException.'
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            }

            if (executeResult.result != null)
            {
                LogSummary(executeResult.result);

                if (m_configuration.PrintObservedAccesses)
                {
                    PrintObservedAccesses(sandboxedProcessInfo.PathTable, executeResult.result);
                }

                if (!TryWriteSandboxedProcessResult(sandboxedProcessInfo.PathTable, executeResult.result))
                {
                    return ExitCode.FailedWriteOutput;
                }
            }

            return executeResult.exitCode;
        }

        private void LogSummary(SandboxedProcessResult result)
        {
            m_logger.LogInfo($"Process exited with exit code '{result.ExitCode}' in {result.PrimaryProcessTimes.TotalWallClockTime.TotalMilliseconds} ms");
        }

        private void PrintObservedAccesses(PathTable pathTable, SandboxedProcessResult result)
        {
            var accesses = new List<ReportedFileAccess>();

            if (result.FileAccesses != null)
            {
                accesses.AddRange(result.FileAccesses);
            }

            if (result.AllUnexpectedFileAccesses != null)
            {
                accesses.AddRange(result.AllUnexpectedFileAccesses);
            }

            m_logger.LogInfo($"{accesses.Count} observed access(es):");

            foreach (var access in accesses)
            {
                m_logger.LogInfo($"{access.GetPath(pathTable)}: {access.Describe()}");
            }
        }

        private bool TryReadSandboxedProcessInfo(out SandboxedProcessInfo? sandboxedProcessInfo)
        {
            SandboxedProcessInfo? localSandboxedProcessInfo = null;

            string sandboxedProcessInfoPath = Path.GetFullPath(m_configuration.SandboxedProcessInfoInputFile);
            m_logger.LogInfo($"Reading sandboxed process info from '{sandboxedProcessInfoPath}'");

            bool success = Helpers.RetryOnFailure(
                attempt => 
                {
                    using FileStream stream = File.OpenRead(sandboxedProcessInfoPath);
                    // TODO: Custom DetoursEventListener?
                    localSandboxedProcessInfo = SandboxedProcessInfo.Deserialize(stream, m_loggingContext, detoursEventListener: null);
                    return true;
                },
                onException: e => m_logger.LogError(e.ToStringDemystified()));

            sandboxedProcessInfo = localSandboxedProcessInfo;

            return success;
        }

        private bool TryReadRemoteSandboxedProcessData(out RemoteData? remoteSandboxedProcessData)
        {
            RemoteData? localRemoteSandboxedProcessData = null;

            string remoteSandboxedProcessDataPath = Path.GetFullPath(m_configuration.RemoteSandboxedProcessDataFile);
            m_logger.LogInfo($"Reading remote sandboxed process data from '{remoteSandboxedProcessDataPath}'");

            bool success = Helpers.RetryOnFailure(
                attempt =>
                {
                    using FileStream stream = File.OpenRead(remoteSandboxedProcessDataPath);
                    localRemoteSandboxedProcessData = RemoteDataExtensions.Deserialize(stream);
                    return true;
                },
                onException: e => m_logger.LogError(e.ToStringDemystified()));

            remoteSandboxedProcessData = localRemoteSandboxedProcessData;

            return success;
        }

        private bool TryReadSandboxedProcessExecutorTestHook(out SandboxedProcessExecutorTestHook? sandboxedProcessExecutorTestHook)
        {
            SandboxedProcessExecutorTestHook? localSandboxedProcessExecutorTestHook = null;

            string sandboxedProcessTestHook = Path.GetFullPath(m_configuration.SandboxedProcessExecutorTestHookFile);
            m_logger.LogInfo($"Reading sandboxed process test hook from '{sandboxedProcessTestHook}'");

            bool success = Helpers.RetryOnFailure(
                attempt => 
                {
                    using FileStream stream = File.OpenRead(sandboxedProcessTestHook);
                    localSandboxedProcessExecutorTestHook = SandboxedProcessExecutorTestHook.Deserialize(stream);
                    return true;
                },
                onException: e => m_logger.LogError(e.ToStringDemystified()));

            sandboxedProcessExecutorTestHook = localSandboxedProcessExecutorTestHook;
            return true;
        }

        private bool TryWriteSandboxedProcessResult(PathTable pathTable, SandboxedProcessResult result)
        {
            Contract.Requires(result != null);

            bool isWindows = !OperatingSystemHelper.IsUnixOS;

            string sandboxedProcessResultOutputPath = Path.GetFullPath(m_configuration.SandboxedProcessResultOutputFile);
            m_logger.LogInfo($"Writing sandboxed process result to '{sandboxedProcessResultOutputPath}'");

            bool success = Helpers.RetryOnFailure(
               attempt =>
               {
                   using FileStream stream = File.OpenWrite(sandboxedProcessResultOutputPath);
                   result.Serialize(stream, writePath);
                   return true;
               },
               onException: e => m_logger.LogError(e.ToStringDemystified()));

            return success;

            // When BuildXL serializes SandboxedProcessInfo, it does not serialize the path table used by SandboxedProcessInfo.
            // On deserializing that info, a new path table is created; see the Deserialize method of SandboxedProcessInfo.
            // Unix sandbox uses the new path table in the deserialized SandboxedProcessInfo to create ManifestPath (AbsolutePath)
            // from reported path access (string) in ReportFileAccess. Without special case, the serialization of SandboxedProcessResult
            // will serialize the AbsolutePath as is. Then, when SandboxedProcessResult is read by BuildXL, BuildXL will not understand
            // the ManifestPath because it is created from a different path table.
            //
            // In Windows, instead of creating ManifestPath from the reported path access (string), ManifestPath is reported from Detours
            // using the AbsolutePath id embedded in the file access manifest. That AbsolutePath id is obtained using the same
            // path table used by BuildXL, and thus BuildXL will understand the ManifestPath serialized by this tool.
            //
            // For Unix, we need to give a special care of path serialization.
            void writePath(BuildXLWriter writer, AbsolutePath path)
            {
                if (isWindows)
                {
                    writer.Write(true);
                    writer.Write(path);
                }
                else
                {
                    writer.Write(false);
                    writer.Write(path.ToString(pathTable));
                }
            }
        }

        private bool TryPrepareSandboxedProcess(SandboxedProcessInfo info, RemoteData? remoteData)
        {
            Contract.Requires(info != null);

            if (!string.IsNullOrEmpty(info.DetoursFailureFile) && FileUtilities.FileExistsNoFollow(info.DetoursFailureFile))
            {
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(info.DetoursFailureFile));
            }

            if (!string.IsNullOrEmpty(info.FileAccessManifest.InternalDetoursErrorNotificationFile)
                && FileUtilities.FileExistsNoFollow(info.FileAccessManifest.InternalDetoursErrorNotificationFile))
            {
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(info.FileAccessManifest.InternalDetoursErrorNotificationFile));
            }

            if (info.GetCommandLine().Length > SandboxedProcessInfo.MaxCommandLineLength)
            {
                m_logger.LogError($"Process command line is longer than {SandboxedProcessInfo.MaxCommandLineLength} characters: {info.GetCommandLine()}");
                return false;
            }

            m_outputErrorObserver = OutputErrorObserver.Create(m_logger, info);
            info.StandardOutputObserver = m_outputErrorObserver.ObserveStandardOutputForWarning;
            info.StandardErrorObserver = m_outputErrorObserver.ObserveStandardErrorForWarning;

            if (!TryPrepareWorkingDirectory(info) 
                || !TryPrepareTemporaryDirectories(info, remoteData) 
                || !TryCleanStaleOuputs(info))
            {
                return false;
            }

            SetSandboxConnectionIfNeeded(info);

            return true;
        }

        private static Possible<UnitValue> PrepareTempDirectory(string tempDirectory, bool cleanDirectoryIfExists = true)
        {
            try
            {
                bool exists = false;

                if (FileUtilities.DirectoryExistsNoFollow(tempDirectory))
                {
                    if (cleanDirectoryIfExists)
                    {
                        FileUtilities.DeleteDirectoryContents(tempDirectory, deleteRootDirectory: false);
                    }

                    exists = true;
                }
                else if (FileUtilities.FileExistsNoFollow(tempDirectory))
                {
                    // We expect to produce a directory, but a file with the same name exists on disk.
                    FileUtilities.DeleteFile(tempDirectory);
                }

                if (!exists)
                {
                    FileUtilities.CreateDirectory(tempDirectory);
                }

                return UnitValue.Unit;
            }
            catch (BuildXLException e)
            {
                return new Failure<string>(e.ToStringDemystified());
            }
        }

        private void SetSandboxConnectionIfNeeded(SandboxedProcessInfo info)
        {
            if (OperatingSystemHelper.IsLinuxOS)
            {
                m_sandboxConnection = new SandboxConnectionLinuxDetours(SandboxConnectionFailureCallback);
            }
            else if (OperatingSystemHelper.IsMacOS)
            {
                m_sandboxConnection = new SandboxConnectionKext(
                    new SandboxConnectionKext.Config
                    {
                        FailureCallback = SandboxConnectionFailureCallback,
                        KextConfig = new Interop.Unix.Sandbox.KextConfig
                        {
                            ReportQueueSizeMB = ReportQueueSizeForKextMB,
#if PLATFORM_OSX
                            EnableCatalinaDataPartitionFiltering = OperatingSystemHelper.IsMacWithoutKernelExtensionSupport
#endif
                        }
                    });
            }

            info.SandboxConnection = m_sandboxConnection;
        }

        private void SandboxConnectionFailureCallback(int status, string description)
        {
            m_sandboxConnection?.Dispose();
            throw new SystemException($"Received unrecoverable error from the sandbox (Code: {status:X}, Description: {description}), please reload the extension and retry.");
        }

        private bool TryPrepareWorkingDirectory(SandboxedProcessInfo info)
        {
            if (!Directory.Exists(info.WorkingDirectory))
            {
                try
                {
                    FileUtilities.CreateDirectory(info.WorkingDirectory);
                }
                catch (BuildXLException e)
                {
                    m_logger.LogError($"Failed to prepare temporary folder '{info.WorkingDirectory}': {e.ToStringDemystified()}");
                    return false;
                }
            }

            return true;
        }

        private bool TryPrepareTemporaryDirectories(SandboxedProcessInfo info, RemoteData? remoteData)
        {
            Contract.Requires(info != null);

            if (info.RedirectedTempFolders != null)
            {
                foreach (var redirection in info.RedirectedTempFolders)
                {
                    var result = PrepareTempDirectory(redirection.target);
                    if (!result.Succeeded)
                    {
                        m_logger.LogError($"Failed to prepare temporary directory '{redirection.target}': {result.Failure.DescribeIncludingInnerFailures()}");
                        return false;
                    }
                }
            }

            foreach (var tmpEnvVar in BuildParameters.DisallowedTempVariables)
            {
                if (info.EnvironmentVariables.ContainsKey(tmpEnvVar))
                {
                    string tempPath = info.EnvironmentVariables[tmpEnvVar];
                    var result = PrepareTempDirectory(tempPath);
                    if (!result.Succeeded)
                    {
                        m_logger.LogError($"Failed to prepare temporary directory '{tempPath}': {result.Failure.DescribeIncludingInnerFailures()}");
                        return false;
                    }   
                }
            }

            string vmSharedTemp = info.EnvironmentVariables.TryGetValue(VmSpecialEnvironmentVariables.VmSharedTemp, null);

            if (!string.IsNullOrEmpty(vmSharedTemp))
            {
                // Ensure that the directory exists, but do not clean if it already exists because the directory is shared by multiple pips.
                var result = PrepareTempDirectory(vmSharedTemp, cleanDirectoryIfExists: false);
                if (!result.Succeeded)
                {
                    m_logger.LogError($"Failed to prepare VM shared temporary directory '{vmSharedTemp}': {result.Failure.DescribeIncludingInnerFailures()}");
                    return false;
                }
            }

            if (remoteData == null)
            {
                return true;
            }

            foreach (var dir in remoteData.TempDirectories)
            {
                var result = PrepareTempDirectory(dir);
                if (!result.Succeeded)
                {
                    m_logger.LogError($"Failed to prepare temporary directory '{dir}': {result.Failure.DescribeIncludingInnerFailures()}");
                    return false;
                }
            }

            return true;
        }

        private async Task<(ExitCode, SandboxedProcessResult?)> ExecuteAsync(SandboxedProcessInfo info)
        {
            m_logger.LogInfo($"Start execution: {info.GetCommandLine()}");

            FileAccessManifest fam = info.FileAccessManifest;

            try
            {
                if (fam.CheckDetoursMessageCount && !OperatingSystemHelper.IsUnixOS)
                {
                    string semaphoreName = !string.IsNullOrEmpty(info.DetoursFailureFile)
                        ? info.DetoursFailureFile.Replace('\\', '_')
                        : "Detours_" + info.PipSemiStableHash.ToString("X16", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString();
                    int maxRetry = 3;

                    // We check this first due to bug#1873910 when executing in a VM on Cloudbuild.
                    // Creating this Semaphore may fail due to a semaphore with the same name already existing.
                    // The reason for this is currently unknown, however since the name for the failures file is
                    // created in SandboxedProcessPipExecutor.GetDetoursInternalErrorFilePath with a unique guid
                    // along with the pip hash it should be safe to dispose because the name is unique to this pip.
#pragma warning disable CA1416 // Validate platform compatibility: Only run on Windows.
                    if (Semaphore.TryOpenExisting(semaphoreName, out var existingSemaphore))
                    {
                        m_logger.LogInfo($"Disposing existing semaphore with name '{semaphoreName}'.");
                        // Calling dispose on this will allow us to create a new semaphore with the same name
                        existingSemaphore.Dispose();
                    }
#pragma warning restore CA1416 // Validate platform compatibility

                    while (!fam.SetMessageCountSemaphore(semaphoreName))
                    {
                        m_logger.LogInfo($"Semaphore '{semaphoreName}' for counting Detours messages is already opened");
                        fam.UnsetMessageCountSemaphore();
                        --maxRetry;
                        if (maxRetry == 0)
                        {
                            break;
                        }

                        semaphoreName += $"_{maxRetry}";
                    }

                    if (maxRetry == 0)
                    {
                        m_logger.LogError($"Semaphore for counting Detours messages cannot be newly created");
                        return (ExitCode.FailedSandboxPreparation, null);
                    }
                }

                using Stream? standardInputStream = TryOpenStandardInputStream(info, out bool succeedInOpeningStdIn);

                if (!succeedInOpeningStdIn)
                {
                    return (ExitCode.FailedSandboxPreparation, null);
                }

                using StreamReader? standardInputReader = standardInputStream == null ? null : new StreamReader(standardInputStream, CharUtilities.Utf8NoBomNoThrow);
                info.StandardInputReader = standardInputReader;

                ISandboxedProcess? process = await StartProcessAsync(info);

                if (process == null)
                {
                    return (ExitCode.FailedStartProcess, null);
                }

                SandboxedProcessResult result = await process.GetResultAsync();

                // Patch result.
                if (m_outputErrorObserver != null)
                {
                    result.WarningCount = m_outputErrorObserver.WarningCount;
                }

                result.LastMessageCount = process.GetLastMessageCount();
                result.DetoursMaxHeapSize = process.GetDetoursMaxHeapSize();
                result.MessageCountSemaphoreCreated = info.FileAccessManifest.MessageCountSemaphore != null;

                return (ExitCode.Success, result);
            }
            finally
            {
                fam.UnsetMessageCountSemaphore();
            }
        }

        private async Task<ISandboxedProcess?> StartProcessAsync(SandboxedProcessInfo info)
        {
            ISandboxedProcess? process = null;
            bool shouldRelaunchProcess = true;
            int processRelaunchCount = 0;

            while (shouldRelaunchProcess)
            {
                try
                {
                    shouldRelaunchProcess = false;
                    process = await SandboxedProcessFactory.StartAsync(info, forceSandboxing: false);
                }
                catch (BuildXLException ex)
                {
                    if (ex.LogEventErrorCode == NativeIOConstants.ErrorFileNotFound)
                    {
                        m_logger.LogError($"Failed to start process: '{info.FileName}' not found");
                        return null;
                    }
                    else if (ex.LogEventErrorCode == NativeIOConstants.ErrorPartialCopy && (processRelaunchCount < ProcessRelauchCountMax))
                    {
                        ++processRelaunchCount;
                        shouldRelaunchProcess = true;

                        m_logger.LogInfo($"Retry to start process for {processRelaunchCount} time(s) due to the following error: {ex.LogEventErrorCode}");

                        // Ensure that process terminates before relaunching it.
                        if (process != null)
                        {
                            try
                            {
                                await process.GetResultAsync();
                            }
                            finally
                            {
                                process.Dispose();
                            }
                        }
                    }
                    else
                    {
                        m_logger.LogError($"Failed to start process '{info.FileName}': {ex.LogEventMessage} ({ex.LogEventErrorCode})");
                        return null;
                    }
                }
            }

            return process;
        }

        private Stream? TryOpenStandardInputStream(SandboxedProcessInfo info, out bool success)
        {
            success = true;

            if (info.StandardInputSourceInfo == null)
            {
                return null;
            }

            try
            {
                if (info.StandardInputSourceInfo.Data != null)
                {
                    return new MemoryStream(CharUtilities.Utf8NoBomNoThrow.GetBytes(info.StandardInputSourceInfo.Data));
                }
                else
                {
                    Contract.Assert(info.StandardInputSourceInfo.File != null);

                    return FileUtilities.CreateAsyncFileStream(
                        info.StandardInputSourceInfo.File,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete);
                }
            }
            catch (BuildXLException ex)
            {
                m_logger.LogError($"Unable to open standard input stream: {ex.ToString()}");
                success = false;
                return null;
            }
        }

        /// <summary>
        /// This function will attempt to clean up some stale outputs from a previous run of this pip
        /// by deleting a set of files specified in <see cref="SandboxedProcessInfo"/>.
        /// </summary>
        /// <returns>True if succeeded</returns>
        private bool TryCleanStaleOuputs(SandboxedProcessInfo info)
        {
            if (info.ExternalVMSandboxedProcessData != null)
            {
                foreach (var staleFile in info.ExternalVMSandboxedProcessData.StaleFilesToClean)
                {
                    // Log who was holding the file from inside the VM
                    if (FileUtilities.TryFindOpenHandlesToFile(staleFile, out var diagnosticData, printCurrentFilePath: true))
                    {
                        m_logger.LogInfo($"Open file handle diagnostic data for stale file '{staleFile}' : '{diagnosticData}'");
                    }
                    else
                    {
                        m_logger.LogInfo($"Unable to find open file handles for stale file '{staleFile}'");
                    }

                    var mayBeDeleted = FileUtilities.TryDeletePathIfExists(staleFile);
                    if (!mayBeDeleted.Succeeded)
                    {
                        m_logger.LogError($"Unable to delete '{staleFile}' on VM with error: {mayBeDeleted.Failure.DescribeIncludingInnerFailures()}");
                        return false;
                    }
                    m_logger.LogInfo($"Deleted stale output: '{staleFile}'");
                }
            }

            return true;
        }
    }
}

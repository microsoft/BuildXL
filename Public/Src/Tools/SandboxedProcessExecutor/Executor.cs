// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.SandboxedProcessExecutor.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.SandboxedProcessExecutor
{
    internal sealed class Executor
    {
        private const int ProcessRelauchCountMax = 5;

        private readonly Configuration m_configuration;
        private readonly LoggingContext m_loggingContext = new LoggingContext("BuildXL.SandboxedProcessExecutor");
        public readonly TrackingEventListener TrackingEventListener = new TrackingEventListener(Events.Log);
        private readonly Stopwatch m_telemetryStopwatch = new Stopwatch();
        private OutputErrorObserver m_outputErrorObserver;

        /// <summary>
        /// Creates an instance of <see cref="Executor"/>.
        /// </summary>
        public Executor(Configuration configuration)
        {
            Contract.Requires(configuration != null);

            m_configuration = configuration;
    }

        public int Run()
        {
            AppDomain.CurrentDomain.UnhandledException +=
                (sender, eventArgs) =>
                {
                    HandleUnhandledFailure(eventArgs.ExceptionObject as Exception);
                };

            TelemetryStartup();

            ExitCode exitCode = RunInternal();

            TelemetryShutdown();

            return (int)exitCode;
        }

        private void HandleUnhandledFailure(Exception exception)
        {
            // Show the exception to the user
            ConsoleError(exception.ToString());
            
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
                AriaV2StaticState.Enable(global::BuildXL.Tracing.AriaTenantToken.Key);
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
                AriaV2StaticState.TryShutDown(TimeSpan.FromSeconds(10), out var telemetryShutdownException);
            }
        }

        internal ExitCode RunInternal()
        {
            if (!TryReadSandboxedProcessInfo(out SandboxedProcessInfo sandboxedProcessInfo))
            {
                return ExitCode.FailedReadInput;
            }

            if (!TryPrepareSandboxedProcess(sandboxedProcessInfo))
            {
                return ExitCode.FailedSandboxPreparation;
            }

            (ExitCode exitCode, SandboxedProcessResult result) executeResult = ExecuteAsync(sandboxedProcessInfo).GetAwaiter().GetResult();

            if (executeResult.result != null)
            {
                if (!TryWriteSandboxedProcessResult(executeResult.result))
                {
                    return ExitCode.FailedWriteOutput;
                }
            }

            return executeResult.exitCode;
        }

        private bool TryReadSandboxedProcessInfo(out SandboxedProcessInfo sandboxedProcessInfo)
        {
            sandboxedProcessInfo = null;

            try
            {
                using (FileStream stream = File.OpenRead(Path.GetFullPath(m_configuration.SandboxedProcessInfoInputFile)))
                {
                    // TODO: Custom DetoursEventListener?
                    sandboxedProcessInfo = SandboxedProcessInfo.Deserialize(stream, m_loggingContext, detoursEventListener: null);
                }
            }
            catch (IOException ioException)
            {
                ConsoleError(ioException.ToString());
                return false;
            }

            return true;
        }

        private bool TryWriteSandboxedProcessResult(SandboxedProcessResult result)
        {
            Contract.Requires(result != null);

            try
            {
                using (FileStream stream = File.OpenWrite(Path.GetFullPath(m_configuration.SandboxedProcessResultOutputFile)))
                {
                    result.Serialize(stream);
                }
            }
            catch (IOException ioException)
            {
                ConsoleError(ioException.ToString());
                return false;
            }

            return true;
        }

        private bool TryPrepareSandboxedProcess(SandboxedProcessInfo info)
        {
            Contract.Requires(info != null);

            FileAccessManifest fam = info.FileAccessManifest;

            if (!string.IsNullOrEmpty(fam.InternalDetoursErrorNotificationFile))
            {
                Analysis.IgnoreResult(FileUtilities.TryDeleteFile(fam.InternalDetoursErrorNotificationFile));
            }

            if (fam.CheckDetoursMessageCount && !OperatingSystemHelper.IsUnixOS)
            {
                string semaphoreName = fam.InternalDetoursErrorNotificationFile.Replace('\\', '_');

                if (!fam.SetMessageCountSemaphore(semaphoreName))
                {
                    ConsoleError($"Semaphore '{semaphoreName}' for counting Detours messages is already opened");
                    return false;
                }
            }

            if (info.GetCommandLine().Length > SandboxedProcessInfo.MaxCommandLineLength)
            {
                ConsoleError($"Process command line is longer than {SandboxedProcessInfo.MaxCommandLineLength} characters: {info.GetCommandLine().Length}");
                return false;
            }

            m_outputErrorObserver = OutputErrorObserver.Create(info);
            info.StandardOutputObserver = m_outputErrorObserver.ObserveOutput;
            info.StandardErrorObserver = m_outputErrorObserver.ObserveError;

            return true;
        }

        private async Task<(ExitCode, SandboxedProcessResult)> ExecuteAsync(SandboxedProcessInfo info)
        {
            try
            {
                using (Stream standardInputStream = TryOpenStandardInputStream(info, out bool succeedInOpeningStdIn))
                {
                    if (!succeedInOpeningStdIn)
                    {
                        return (ExitCode.FailedSandboxPreparation, null);
                    }

                    using (StreamReader standardInputReader = standardInputStream == null ? null : new StreamReader(standardInputStream, CharUtilities.Utf8NoBomNoThrow))
                    {
                        info.StandardInputReader = standardInputReader;

                        ISandboxedProcess process = await StartProcessAsync(info);

                        if (process == null)
                        {
                            return (ExitCode.FailedStartProcess, null);
                        }

                        SandboxedProcessResult result = await process.GetResultAsync();

                        // Patch result.
                        result.WarningCount = m_outputErrorObserver.WarningCount;
                        result.LastMessageCount = process.GetLastMessageCount();
                        result.DetoursMaxHeapSize = process.GetDetoursMaxHeapSize();
                        result.MessageCountSemaphoreCreated = info.FileAccessManifest.MessageCountSemaphore != null;

                        return (ExitCode.Success, result);
                    }
                }
            }
            finally
            {
                info.FileAccessManifest.UnsetMessageCountSemaphore();
            }
        }

        private async Task<ISandboxedProcess> StartProcessAsync(SandboxedProcessInfo info)
        {
            ISandboxedProcess process = null;
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
                        ConsoleError($"Failed to start process: '{info.FileName}' not found");
                        return null;
                    }
                    else if (ex.LogEventErrorCode == NativeIOConstants.ErrorPartialCopy && (processRelaunchCount < ProcessRelauchCountMax))
                    {
                        ++processRelaunchCount;
                        shouldRelaunchProcess = true;

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
                        ConsoleError($"Failed to start process '{info.FileName}': {ex.LogEventMessage} ({ex.LogEventErrorCode})");
                        return null;
                    }
                }
            }

            return process;
        }

        private Stream TryOpenStandardInputStream(SandboxedProcessInfo info, out bool success)
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
                ConsoleError($"Unable to open standard input stream: {ex.ToString()}");
                success = false;
                return null;
            }
        }

        private void ConsoleError(string errorMessage)
        {
            ConsoleColor original = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(errorMessage);
            Console.ForegroundColor = original;
        }
    }
}

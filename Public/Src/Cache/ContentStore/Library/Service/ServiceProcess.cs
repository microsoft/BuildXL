// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Service
{

    /// <summary>
    ///     Helper for managing the launching and shutdown of the cache in a separate process.
    /// </summary>
    public class ServiceProcess : IStartupShutdown
    {
        /// <nodoc />
        protected virtual Tracer Tracer { get; } = new Tracer(nameof(ServiceProcess));

        /// <nodoc />
        protected readonly ServiceConfiguration Configuration;

        /// <nodoc />
        protected readonly string Scenario;

        /// <nodoc />
        protected readonly int WaitForServerReadyTimeoutMs;

        /// <nodoc />
        protected readonly int WaitForExitTimeoutMs;

        /// <nodoc />
        protected readonly bool LogAutoFlush;

        private ProcessUtility? _process;
        private string? _args;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ServiceProcess"/> class.
        /// </summary>
        public ServiceProcess
            (
            ServiceConfiguration configuration,
            string scenario,
            int waitForServerReadyTimeoutMs,
            int waitForExitTimeoutMs,
            bool logAutoFlush = true
            )
        {
            Contract.Requires(configuration != null);

            Configuration = configuration;
            Scenario = scenario;
            WaitForServerReadyTimeoutMs = waitForServerReadyTimeoutMs;
            WaitForExitTimeoutMs = waitForExitTimeoutMs;
            LogAutoFlush = logAutoFlush;
        }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public async Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            BoolResult result = BoolResult.Success;
            Tracer.Debug(context, "Starting service process");

            await Task.Run(() =>
            {
                var appExePath = GetExecutablePath();

                _args = GetCommandLineArgs();

                Tracer.Debug(context, $"Running cmd=[{appExePath} {_args}]");

                const bool CreateNoWindow = true;
                _process = new ProcessUtility(appExePath.Path, _args, CreateNoWindow);

                _process.Start();

                string processOutput;
                if (_process == null)
                {
                    processOutput = "[Process could not start]";
                    result = new BoolResult(processOutput);
                }
                else if (CreateNoWindow)
                {
                    if (_process.HasExited)
                    {
                        if (_process.WaitForExit(5000))
                        {
                            throw new InvalidOperationException(_process.GetLogs());
                        }
                        else
                        {
                            throw new InvalidOperationException("Process or either wait handle timed out. " + _process.GetLogs());
                        }
                    }
                    else
                    {
                        processOutput = $"[Process {_process.Id} is still running]";
                    }
                }

                Tracer.Debug(context, "Process output: " + processOutput);
            });

            if (result.Succeeded && !LocalContentServer.EnsureRunning(context, Scenario, WaitForServerReadyTimeoutMs))
            {
                result = new BoolResult($"Failed to detect server ready in separate process for scenario {Scenario}. Process has {(_process!.HasExited ? string.Empty : "not")} exited.");
            }

            StartupCompleted = true;
            return result;
        }

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            Tracer.Debug(context, $"Stopping service process {_process?.Id} for scenario {Scenario}");

            if (_process == null)
            {
                return BoolResult.Success;
            }

            await Task.Run(() =>
            {
                IpcUtilities.SetShutdown(Scenario);

                if (!_process.WaitForExit(WaitForExitTimeoutMs))
                {
                    Tracer.Warning(context, "Service process failed to exit, killing hard");
                    try
                    {
                        _process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // the process may have exited,
                        // in this case ignore the exception
                    }
                }
            });

            ShutdownCompleted = true;

            if (_process.ExitCode.HasValue && (_process.ExitCode != 0 || _process.GetStdErr().Length != 0))
            {
                return new BoolResult($"Process exited with code {_process.ExitCode}. Command line args: {_args}. StdErr: {_process.GetStdErr()} StdOut: {_process.GetStdOut()}");
            }

            return BoolResult.Success;
        }

        /// <summary>
        /// <see cref="ProcessUtility.GetLogs"/>
        /// </summary>
        public string? GetLogs()
        {
            return _process?.GetLogs();
        }

        /// <nodoc />
        protected virtual AbsolutePath GetExecutablePath()
        {
            AbsolutePath appExeDirPath = new AbsolutePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
            return appExeDirPath / (OperatingSystemHelper.IsUnixOS ? "ContentStoreApp" : "ContentStoreApp.exe");
        }

        /// <nodoc />
        protected virtual string GetCommandLineArgs()
        {
            return Configuration.GetCommandLineArgs(scenario: Scenario);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}

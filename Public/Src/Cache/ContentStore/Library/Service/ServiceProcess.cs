// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     Helper for managing the launching and shutdown of the cache in a separate process.
    /// </summary>
    public sealed class ServiceProcess : IStartupShutdown
    {
        private readonly ServiceConfiguration _configuration;
        private readonly LocalServerConfiguration _localContentServerConfiguration;
        private readonly string _scenario;
        private readonly int _waitForServerReadyTimeoutMs;
        private readonly int _waitForExitTimeoutMs;
        private readonly bool _logAutoFlush;
        private ProcessUtility _process;
        private string _args;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ServiceProcess"/> class.
        /// </summary>
        public ServiceProcess
            (
            ServiceConfiguration configuration,
            LocalServerConfiguration localContentServerConfiguration,
            string scenario,
            int waitForServerReadyTimeoutMs,
            int waitForExitTimeoutMs,
            bool logAutoFlush = true
            )
        {
            Contract.Requires(configuration != null);

            _configuration = configuration;
            _localContentServerConfiguration = localContentServerConfiguration;
            _scenario = scenario;
            _waitForServerReadyTimeoutMs = waitForServerReadyTimeoutMs;
            _waitForExitTimeoutMs = waitForExitTimeoutMs;
            _logAutoFlush = logAutoFlush;
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
            context.Debug("Starting service process");

            await Task.Run(() =>
            {
                AbsolutePath appExeDirPath = new AbsolutePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                var appExePath = appExeDirPath / (OperatingSystemHelper.IsUnixOS ? "ContentStoreApp" : "ContentStoreApp.exe");

                _args = _configuration.GetCommandLineArgs(scenario: _scenario);

                context.Debug($"Running cmd=[{appExePath} {_args}]");

                const bool createNoWindow = true;
                _process = new ProcessUtility(appExePath.Path, _args, createNoWindow);

                _process.Start();

                string processOutput;
                if (_process == null)
                {
                    processOutput = "[Process could not start]";
                    result = new BoolResult(processOutput);
                }
                else if (createNoWindow)
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

                context.Debug("Process output: " + processOutput);
            });

            if (result.Succeeded && !LocalContentServer.EnsureRunning(context, _scenario, _waitForServerReadyTimeoutMs))
            {
                result = new BoolResult($"Failed to detect server ready in separate process for scenario {_scenario}. Process has {(_process.HasExited ? string.Empty : "not")} exited.");
            }

            StartupCompleted = true;
            return result;
        }

        /// <inheritdoc />
        public async Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            context.Debug($"Stopping service process {_process.Id} for scenario {_scenario}");

            await Task.Run(() =>
            {
                IpcUtilities.SetShutdown(_scenario);

                if (!_process.WaitForExit(_waitForExitTimeoutMs))
                {
                    context.Warning("Service process failed to exit, killing hard");
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
        public string GetLogs()
        {
            return _process?.GetLogs();
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}

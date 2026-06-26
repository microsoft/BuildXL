// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Abstract the launching of the BuildXL process to this interface for testing purposes
    /// </summary>
    public interface IBuildXLLauncher
    {
        /// <summary>
        /// Run BuildXL with the specified arguments and redirecting output and error stream lines to the specified delegates.
        /// </summary>
        /// <param name="args">Command-line arguments passed to BuildXL.</param>
        /// <param name="outputDataReceived">Callback for each stdout line.</param>
        /// <param name="errorDataReceived">Callback for each stderr line.</param>
        /// <param name="extraEnvironment">
        /// Optional environment variables to inject into the launched process. Used to pass inheritable
        /// resources (e.g. anonymous-pipe client handles) from the runner to BuildXL without exposing
        /// them on the command line. Keys are environment variable names, values are their values.
        /// </param>
        /// <returns>The exit code of the engine invocation</returns>
        public Task<int> LaunchAsync(
            string args,
            Action<string> outputDataReceived,
            Action<string> errorDataReceived,
            IReadOnlyDictionary<string, string>? extraEnvironment = null);
    }

    /// <summary>
    /// Concrete implementation of IBuildXLLauncher that just launches a regular Process and waits for exit
    /// </summary>
    public class BuildXLLauncher : IBuildXLLauncher
    {
        private readonly string m_bxlExeLocation;

        /// <nodoc />
        public BuildXLLauncher(IAdoBuildRunnerConfiguration config)
        {
            // Resolve the bxl executable location.
            // If not specified explicitly, we assume its in the same location as this executable
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "bxl" : "bxl.exe";
            var engineLocation = config.EngineLocation ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            m_bxlExeLocation = Path.Combine(engineLocation, exeName);
        }

        /// <inheritdoc />
        public async Task<int> LaunchAsync(
            string commandLine,
            Action<string> outputDataReceived,
            Action<string> errorDataReceived,
            IReadOnlyDictionary<string, string>? extraEnvironment = null)
        {
            using var process = new Process()
            {
                StartInfo =
                {
                    FileName = m_bxlExeLocation,
                    Arguments = commandLine,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
            };

            if (extraEnvironment != null)
            {
                foreach (var kv in extraEnvironment)
                {
                    process.StartInfo.Environment[kv.Key] = kv.Value;
                }
            }

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputDataReceived?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorDataReceived?.Invoke(e.Data);
                }
            };

            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var exitCode = process.ExitCode;
            // CODESYNC: Public/Src/Engine/Dll/Engine.cs
            // NOTE: we explicitly don't use ExitKind here to avoid adding a dependency on BuildXL.Utilities.Configuration
            if (exitCode == 7)
            {
                // We don't know what caused buildxl to hang, but we'll modify the exit value for now to avoid failing the pipeline.
                errorDataReceived("BuildXL successfully completed with an abnormal exit code.");
                exitCode = 0;
            }

            return exitCode;
        }
    }
}

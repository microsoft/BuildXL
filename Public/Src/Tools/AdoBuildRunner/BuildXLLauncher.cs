// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace AdoBuildRunner
{
    /// <summary>
    /// Abstract the launching of the BuildXL process to this interface for testing purposes
    /// </summary>
    public interface IBuildXLLauncher
    {
        /// <summary>
        /// Run BuildXL with the specified arguments and redirecting output and error stream lines to the specified delegates
        /// </summary>
        /// <returns>The exit code of the engine invocation</returns>
        public Task<int> LaunchAsync(string args, Action<string> outputDataReceived, Action<string> errorDataRecieved);
    }

    /// <summary>
    /// Concrete implementation of IBuildXLLauncher that just launches a regular Process and waits for exit
    /// </summary>
    public class BuildXLLauncher : IBuildXLLauncher
    {
        private readonly string m_bxlExeLocation;
        
        /// <nodoc />
        public BuildXLLauncher()
        {
            // Resolve the bxl executable location. For now, we assume its in the same location as this executable
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "bxl" : "bxl.exe";
            m_bxlExeLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, exeName);
        }

        /// <inheritdoc />
        public async Task<int> LaunchAsync(string commandLine, Action<string> outputDataReceived, Action<string> errorDataRecieved)
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
                    errorDataRecieved?.Invoke(e.Data);
                }
            };

            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return process.ExitCode;
        }
    }
}

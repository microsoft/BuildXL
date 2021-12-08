// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Sbom.Contracts;
using Newtonsoft.Json;

namespace BuildXL.Utilities.SBOMUtilities
{
    /// <summary>
    /// Converts from the component detection package format to the SBOMPackage format.
    /// </summary>
    public class ComponentDetectionConverter
    {
        /// <summary>
        /// Converts a bcde-output.json file produced by component detection to a list of SBOMPackage objects for SPDX.
        /// </summary>
        /// <param name="bcdeOutputPath">Path to bcde-output.json file.</param>
        /// <param name="logger">Logger to log any potential warnings during conversion.</param>
        /// <param name="packages">List of <see cref="SBOMPackage"/> objects to be returned.</param>
        /// <returns>Returns false if package conversion was unsuccessful or only partially successful.</returns>
        public static bool TryConvert(string bcdeOutputPath, SBOMConverterLogger logger, out IEnumerable<SBOMPackage> packages)
        {
            packages = null;
            if (string.IsNullOrWhiteSpace(bcdeOutputPath) || !File.Exists(bcdeOutputPath))
            {
                logger.Error($"bcde-output.json file does not exist at path '{bcdeOutputPath}'.");
                return false;
            }

            // CODESYNC: Public\Src\Tools\SBOMConverter\Tool.SBOMConverter.dsc
            // Deployment location
            var sbomConverterToolPath = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "tools", "SBOMConverter", "SBOMConverter.exe");
            var resultPath = Path.Combine(Directory.GetParent(bcdeOutputPath).FullName, "conversionresult.json");

            try
            {
                var arguments = $"/bcdeOutputPath:{bcdeOutputPath} /resultPath:{resultPath}";
                var startInfo = new ProcessStartInfo
                {
                    FileName = sbomConverterToolPath,
                    Arguments = arguments,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    ErrorDialog = false
                };
                var timeout = TimeSpan.FromMinutes(15);

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    logger.Error($"Unable to start SBOMConverter process at path '{sbomConverterToolPath}'.");
                    return false;
                }

                process.OutputDataReceived += (o, e) => logger.Info(e.Data);
                process.ErrorDataReceived += (o, e) => logger.Error(e.Data);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (CancellationToken.None.Register(() => KillProcess(process)))
                {
                    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        KillProcess(process);
                        logger.Error($"SBOMConverter process timed out.");
                        return false;
                    }

                    process.WaitForExit();
                }

                process.CancelErrorRead();
                process.CancelOutputRead();

                if (process.ExitCode == 0)
                {
                    packages = JsonConvert.DeserializeObject<IEnumerable<SBOMPackage>>(File.ReadAllText(resultPath));
                }
                else
                {
                    // Errors have already been logged by the SBOMConverter
                    logger.Error($"SBOMConverter exited with non-zero exit code '{process.ExitCode}'.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to execute SBOMConverter wth exception: {ex}");
                return false;
            }

            return true;
        }

        /// <nodoc/>
        private static void KillProcess(Process process)
        {
            if (process.HasExited)
            {
                return;
            }

            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // the process may have exited,
                // in this case ignore the exception
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if FEATURE_ANYBUILD_PROCESS_REMOTING

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AnyBuild;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// AnyBuild client installer.
    /// </summary>
    public class AnyBuildInstaller : IRemoteProcessManagerInstaller
    {
        /// <summary>
        /// Installation rings.
        /// </summary>
        public enum Ring
        {
            /// <summary>
            /// Dogfood.
            /// </summary>
            Dogfood,

            /// <summary>
            /// Production.
            /// </summary>
            Production
        }

        private const string DefaultAnyBuildClientInstallSource = "https://anybuild.azureedge.net/clientreleases";

        /// <summary>
        /// Minimum required version of AnyBuild.
        /// </summary>
        /// <remarks>
        /// If minumum is not satisfied, BuildXL will keep reinstalling AnyBuild.
        /// 
        /// Known versions:
        /// - 98a7fbfa_20220314.3_149776
        /// - b36bd90f_20220319.3_151742
        /// </remarks>
        internal static readonly AnyBuildVersion MinRequiredVersion = AnyBuildVersion.Create("733e1fd5_20220330.10_156353")!;

        private static readonly string s_installRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "AnyBuild");

        private readonly string m_source;
        private readonly Ring m_ring;
        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// Creates an instance of <see cref="AnyBuildInstaller"/>.
        /// </summary>
        /// <param name="source">Source of AnyBuild client.</param>
        /// <param name="ring">Ring.</param>
        /// <param name="loggingContext">Logging context.</param>
        public AnyBuildInstaller(string source, Ring ring, LoggingContext loggingContext)
        {
            m_source = source;
            m_ring = ring;
            m_loggingContext = loggingContext;
        }

        /// <summary>
        /// Creates an instance of <see cref="AnyBuildInstaller"/>.
        /// </summary>
        /// <param name="sourceAndRing">Source and ring of AnyBuild client.</param>
        /// <param name="loggingContext">Logging context.</param>
        public AnyBuildInstaller(string sourceAndRing, LoggingContext loggingContext)
        {
            (m_source, m_ring) = ParseSourceAndRing(sourceAndRing);
            m_loggingContext = loggingContext;
        }

        /// <summary>
        /// Creates an instance of <see cref="AnyBuildInstaller"/>.
        /// </summary>
        /// <param name="loggingContext">Logging context.</param>
        public AnyBuildInstaller(LoggingContext loggingContext)
            : this(EngineEnvironmentSettings.AnyBuildClientSource.Value ?? DefaultAnyBuildClientInstallSource, loggingContext)
        {
        }

        /// <summary>
        /// Parses source and ring in URI.
        /// </summary>
        /// <remarks>
        /// An example of <paramref name="sourceAndRing"/> is 'https://anybuild.azureedge.net/clientreleases?ring=Dogfood'
        /// </remarks>
        private static (string, Ring) ParseSourceAndRing(string sourceAndRing)
        {
            int indexOfQuestionMark = sourceAndRing.LastIndexOf('?');
            if (indexOfQuestionMark == -1)
            {
                return (sourceAndRing, Ring.Production);
            }

            string source = sourceAndRing.Substring(0, indexOfQuestionMark);

            // Use Production ring as default.
            Ring ring = Ring.Production;

            if ((indexOfQuestionMark + 1) >= sourceAndRing.Length)
            {
                return (source, ring);
            }

            string[] ringKvp = sourceAndRing.Substring(indexOfQuestionMark + 1).Split('=');
            if (ringKvp.Length != 2 || !string.Equals(ringKvp[0], "ring", StringComparison.OrdinalIgnoreCase))
            {
                return (source, ring);
            }

            if (!Enum.TryParse(ringKvp[1], true, out Ring parsedRing))
            {
                return (source, ring);
            }

            return (source, parsedRing);
        }

        /// <summary>
        /// Installs AnyBuild client.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="forceInstall">Force installation by cleaning existing installation when set to true.</param>
        public Task<bool> InstallAsync(CancellationToken token, bool forceInstall = false)
        {
            if (EngineEnvironmentSettings.AnyBuildInstallDir.Value != null)
            {
                // Do not perform installation if user specifies a particular AnyBuild installation.
                return BoolTask.True;
            }

            if (OperatingSystemHelper.IsWindowsOS)
            {
                return InstallWinAsync(token, forceInstall);
            }

            throw new NotSupportedException($"{nameof(AnyBuildInstaller)} does not support {Environment.OSVersion.Platform}");
        }

        private async Task<bool> InstallWinAsync(CancellationToken token, bool forceInstall = false)
        {
            if (!ShouldInstallWin(MinRequiredVersion, forceInstall, out string reason))
            {
                return true;
            }

            try
            {
                if (Directory.Exists(s_installRoot))
                {
                    Directory.Delete(s_installRoot, true);
                }
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.FailedInstallingAnyBuildClient(m_loggingContext, $"Failed to clean up installation root '{s_installRoot}': {ex}");
                return false;
            }

            Tracing.Logger.Log.InstallAnyBuildClientDetails(m_loggingContext, m_source, m_ring.ToString(), reason);
            Tracing.Logger.Log.InstallAnyBuildClient(m_loggingContext, m_source, m_ring.ToString());

            string? bootstrapperFile = await DownloadBootstrapperWinAsync(token);

            if (bootstrapperFile == null)
            {
                return false;
            }

            return await ExecuteBootstrapper(bootstrapperFile);
        }

        private async Task<bool> ExecuteBootstrapper(string bootstrapperFile)
        {
            var output = new StringBuilder(512);
            var error = new StringBuilder(512);

            string powerShellExe = !string.IsNullOrEmpty(EngineEnvironmentSettings.AnyBuildPsPath.Value)
                ? EngineEnvironmentSettings.AnyBuildPsPath.Value
                : "powershell.exe";
            string powerShellArgs = $"-NoProfile -ExecutionPolicy ByPass -File \"{bootstrapperFile}\" {m_source} {m_ring}";

            Tracing.Logger.Log.ExecuteAnyBuildBootstrapper(m_loggingContext, $"{powerShellExe} {powerShellArgs}");

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo(powerShellExe, powerShellArgs)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    ErrorDialog = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                },
                EnableRaisingEvents = false,
            };

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    output.AppendLine(eventArgs.Data);
                }
            };

            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                {
                    error.AppendLine(eventArgs.Data);
                }
            };

            try
            {
                bool processStarted = process.Start();

                if (!processStarted)
                {
                    Tracing.Logger.Log.FailedInstallingAnyBuildClient(m_loggingContext, "Failed to execute bootstrapper script");
                    return false;
                }

#if NET5_0_OR_GREATER
                await process.WaitForExitAsync();
#else
#pragma warning disable AsyncFixer02
                await Task.Run(() => process.WaitForExit());
#pragma warning restore AsyncFixer02
#endif

                if (process.ExitCode != 0)
                {
                    Tracing.Logger.Log.FailedInstallingAnyBuildClient(
                        m_loggingContext,
                        error.ToString() + " (see log for details)");
                }

                var result = new string[]
                {
                    $"Exit code: {process.ExitCode}",
                    $"StdOut: {output}",
                    $"StdErr: {error}"
                };

                Tracing.Logger.Log.FinishedInstallAnyBuild(
                    m_loggingContext,
                    Environment.NewLine + string.Join(Environment.NewLine, result));

                return process.ExitCode == 0;
            }
            catch (Exception e)
            {
                Tracing.Logger.Log.FailedInstallingAnyBuildClient(m_loggingContext, e.ToString());
                return false;
            }
        }

        private async Task<string?> DownloadBootstrapperWinAsync(CancellationToken token)
        {
            // PowerShell requires the extension to be .ps1.
            string bootstrapperFile = Path.ChangeExtension(Path.GetTempFileName(), ".ps1");

            try
            {
                // Cancellation token on GetStreamAsync is only available starting .NET5.
                using (var httpClient = new HttpClient())
#if NET5_0_OR_GREATER
                using (var stream = await httpClient.GetStreamAsync($"{m_source}/bootstrapper.ps1", token))
#else
                using (var stream = await httpClient.GetStreamAsync($"{m_source}/bootstrapper.ps1"))
#endif
                using (var bootstrapper = new FileStream(bootstrapperFile, FileMode.Create))
                {
#if NET5_0_OR_GREATER
                    await stream.CopyToAsync(bootstrapper, token);
#else
                    await stream.CopyToAsync(bootstrapper);
#endif
                }

                return bootstrapperFile;
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.FailedDownloadingAnyBuildClient(m_loggingContext, ex.ToString());
                return null;
            }
        }

        private static bool ShouldInstallWin(AnyBuildVersion version, bool forceInstall, out string reason) =>
            ShouldInstall(
                s_installRoot,
                "AnyBuild.cmd",
                "Current.txt",
                version,
                forceInstall,
                out reason);

        internal static bool ShouldInstall(
            string installRoot,
            string mainFileName,
            string versionFileName,
            AnyBuildVersion minRequiredVersion,
            bool forceInstall,
            out string reason)
        {
            reason = string.Empty;

            forceInstall |= EngineEnvironmentSettings.AnyBuildForceInstall.Value;

            if (forceInstall)
            {
                reason = "Force installation is enabled";
                return true;
            }

            if (!Directory.Exists(installRoot))
            {
                reason = $"Installation directory '{installRoot}' not found";
                return true;
            }

            string abMain = Path.Combine(installRoot, mainFileName);

            if (!File.Exists(abMain))
            {
                reason = $"AnyBuild main file '{mainFileName}' in installation directory '{installRoot}' not found";
                return true;
            }

            if (EngineEnvironmentSettings.AnyBuildSkipClientVersionCheck.Value)
            {
                return false;
            }

            string versionFile = Path.Combine(installRoot, versionFileName);
            
            if (!File.Exists(versionFile))
            {
                reason = $"AnyBuild version file '{versionFileName}' in installation directory '{installRoot}' not found";
                return true;
            }

            string versionPath = File.ReadAllText(versionFile);

            if (string.IsNullOrEmpty(versionPath))
            {
                reason = $"AnyBuild version cannot be found in '{versionFile}'";
                return true;
            }

            AnyBuildVersion? currentVersion = AnyBuildVersion.Create(Path.GetFileName(versionPath));

            if (currentVersion == null)
            {
                reason = $"Malformed AnyBuild version '{Path.GetFileName(versionPath)}' found in '{versionFile}'";
                return true;
            }

            if (AnyBuildVersion.Comparer.Compare(currentVersion, minRequiredVersion) < 0)
            {
                reason = $"Current AnyBuild version '{currentVersion}' does not meet the minimum required version '{minRequiredVersion}'";
                return true;
            }

            string abDir = Path.Combine(installRoot, currentVersion.ToString());

            if (!Directory.Exists(abDir))
            {
                reason = $"AnyBuild directory for version '{currentVersion}' not found in '{installRoot}'";
                return true;
            }

            return false;
        }
    }
}

#endif
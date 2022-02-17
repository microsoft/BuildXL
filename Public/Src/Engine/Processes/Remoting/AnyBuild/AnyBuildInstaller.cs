// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
            string abDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "AnyBuild");

            string abCmd = Path.Combine(abDir, "AnyBuild.cmd");

            forceInstall |= EngineEnvironmentSettings.AnyBuildForceInstall.Value;

            if (forceInstall && Directory.Exists(abDir))
            {
                Directory.Delete(abDir, true);
            }

            if (File.Exists(abCmd))
            {
                return true;
            }

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

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo(
                    "powershell.exe",
                    $"-NoProfile -ExecutionPolicy ByPass -File \"{bootstrapperFile}\" {m_source} {m_ring}")
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
                    await stream.CopyToAsync(bootstrapper);
                }

                return bootstrapperFile;
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.FailedDownloadingAnyBuildClient(m_loggingContext, ex.ToString());
                return null;
            }
        }
    }
}

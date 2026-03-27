// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Captures a memory dump of the BuildXL process when a configured trigger condition is met.
    /// </summary>
    /// <remarks>
    /// Prefers the bundled <c>dotnet-gcdump</c> tool (deployed alongside bxl.exe) which produces small .gcdump
    /// files (typically 50-200 MB) containing the managed heap object graph (types, sizes, retention paths),
    /// analyzable with Visual Studio, PerfView, or <c>dotnet-gcdump report</c>.
    ///
    /// If the bundled tool is not available or fails, falls back to a full process dump via
    /// <see cref="ProcessDumper.TryDumpProcess(Process, string, out Exception, bool, Action{string})"/>.
    /// The full dump is much larger (proportional to process working set) but can be analyzed with
    /// <c>dotnet-dump analyze</c>, WinDbg, or Visual Studio.
    ///
    /// The trigger fires at most once per build to avoid repeated dumps.
    /// </remarks>
    public class EngineDumpCollector
    {
        /// <summary>
        /// Timeout for the dotnet-gcdump collection process.
        /// GC heap snapshot of a very large process can take many minutes.
        /// </summary>
        private static readonly TimeSpan s_collectionTimeout = TimeSpan.FromMinutes(20);

        private readonly EngineDumpTrigger m_trigger;
        private readonly string m_logsDirectory;
        private readonly LoggingContext m_loggingContext;
        private int m_hasDumped;

        /// <summary>
        /// Creates a new EngineDumpCollector.
        /// </summary>
        /// <param name="trigger">The trigger configuration.</param>
        /// <param name="logsDirectory">The directory to write the dump file to.</param>
        /// <param name="loggingContext">The logging context for status messages.</param>
        public EngineDumpCollector(EngineDumpTrigger trigger, string logsDirectory, LoggingContext loggingContext)
        {
            m_trigger = trigger;
            m_logsDirectory = logsDirectory;
            m_loggingContext = loggingContext;
        }

        /// <summary>
        /// Whether the trigger is enabled.
        /// </summary>
        public bool IsEnabled => m_trigger.IsEnabled;

        /// <summary>
        /// Whether a dump has already been captured this build.
        /// </summary>
        public bool HasDumped => Volatile.Read(ref m_hasDumped) != 0;

        /// <summary>
        /// Checks whether the trigger condition is met and captures a dump if so.
        /// This method is safe to call from any thread and will only capture one dump per build.
        /// </summary>
        /// <param name="processMemoryMb">Current process memory in MB (working set).</param>
        /// <param name="elapsedSeconds">Seconds elapsed since execution start.</param>
        /// <param name="buildPercentage">Build completion percentage (0-100).</param>
        public void CheckTriggerAndDump(int processMemoryMb, double elapsedSeconds, int buildPercentage)
        {
            if (!IsEnabled || HasDumped)
            {
                return;
            }

            bool shouldDump = m_trigger.Kind switch
            {
                EngineDumpTriggerKind.MemoryMb => processMemoryMb >= m_trigger.Value,
                EngineDumpTriggerKind.TimeSec => elapsedSeconds >= m_trigger.Value,
                EngineDumpTriggerKind.BuildPercentage => buildPercentage >= m_trigger.Value,
                _ => false,
            };

            if (!shouldDump)
            {
                return;
            }

            // One-shot guard: only the first thread to swap 0 to 1 proceeds.
            if (Interlocked.CompareExchange(ref m_hasDumped, 1, 0) != 0)
            {
                return;
            }

            CaptureDump();
        }

        /// <summary>
        /// Performs the actual dump capture. Virtual to allow tests to verify trigger logic without
        /// spawning expensive dump processes.
        /// </summary>
        protected virtual void CaptureDump()
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

            Tracing.Logger.Log.EngineDumpCollectorTriggered(m_loggingContext, m_trigger.TriggerReason, m_logsDirectory);

            if (TryCaptureEngineDump(timestamp))
            {
                return;
            }

            // Fallback: full process dump via MiniDumpWriteDump
            CaptureFullDump(timestamp);
        }

        /// <summary>
        /// Attempts to capture a .gcdump via the bundled dotnet-gcdump tool.
        /// Returns true if the dump was captured successfully, false if the tool is not available or failed.
        /// </summary>
        private bool TryCaptureEngineDump(string timestamp)
        {
            string dumpFileName = $"BxlEngineDump_{timestamp}.gcdump";
            string dumpPath = Path.Combine(m_logsDirectory, dumpFileName);

            try
            {
                int pid = Process.GetCurrentProcess().Id;

                // Find the bundled dotnet-gcdump.dll next to the running assembly:
                //   <bxl_dir>/tools/dotnet-gcdump/dotnet-gcdump.dll
                // CodeSync: deployment path must match BuildXL.Engine.dsc runtimeContent (tools/dotnet-gcdump subfolder)
                string bxlDirectory = Directory.GetParent(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly())).FullName;
                string gcdumpDllPath = Path.Combine(bxlDirectory, "tools", "dotnet-gcdump", "dotnet-gcdump.dll");

                if (!File.Exists(gcdumpDllPath))
                {
                    Tracing.Logger.Log.EngineDumpCollectorFailed(m_loggingContext, $"Bundled dotnet-gcdump not found at: {gcdumpDllPath}");
                    return false;
                }

                string dotnetPath = FindDotnetPath();
                if (dotnetPath == null)
                {
                    Tracing.Logger.Log.EngineDumpCollectorFailed(m_loggingContext, "Could not locate dotnet runtime host");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = dotnetPath,
                    Arguments = $"exec \"{gcdumpDllPath}\" collect --process-id {pid} --output \"{dumpPath}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Tracing.Logger.Log.EngineDumpCollectorFailed(m_loggingContext, $"Failed to start dotnet-gcdump process: Process.Start returned null (FileName={dotnetPath})");
                    return false;
                }

                // Read stderr asynchronously to avoid deadlock if the pipe buffer fills.
                string stderr = null;
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        stderr = (stderr == null) ? e.Data : stderr + Environment.NewLine + e.Data;
                    }
                };
                process.BeginErrorReadLine();

                bool exited = process.WaitForExit((int)s_collectionTimeout.TotalMilliseconds);
                if (!exited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception killEx)
                    {
                        // Best effort — log but don't fail
                        Tracing.Logger.Log.EngineDumpCollectorFailed(m_loggingContext, $"Failed to kill timed-out dotnet-gcdump process: {killEx}");
                    }

                    Tracing.Logger.Log.EngineDumpCollectorFailed(
                        m_loggingContext,
                        $"dotnet-gcdump timed out after {s_collectionTimeout.TotalMinutes} minutes");
                    return false;
                }

                if (process.ExitCode == 0 && File.Exists(dumpPath))
                {
                    var fileInfo = new FileInfo(dumpPath);
                    Tracing.Logger.Log.EngineDumpCollectorCompleted(m_loggingContext, $"{dumpPath} ({fileInfo.Length / (1024 * 1024)} MB)");
                    return true;
                }

                Tracing.Logger.Log.EngineDumpCollectorFailed(
                    m_loggingContext,
                    $"dotnet-gcdump exited with code {process.ExitCode}. {stderr}");
                return false;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // dotnet not found — caller will fall back
                Tracing.Logger.Log.EngineDumpCollectorFailed(m_loggingContext, $"Could not launch dotnet-gcdump: {ex}");
                return false;
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.EngineDumpCollectorFailed(m_loggingContext, $"dotnet-gcdump failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Locates the dotnet runtime host executable.
        /// bxl.exe is deployed as a self-contained app so we cannot rely on the running runtime
        /// directory to find a standalone dotnet.exe. Instead we check well-known env vars and
        /// standard install locations.
        /// </summary>
        private static string FindDotnetPath()
        {
            string dotnetExeName = OperatingSystemHelper.IsWindowsOS ? "dotnet.exe" : "dotnet";

            // 1. DOTNET_HOST_PATH — set by the dotnet host when it launches framework-dependent apps
            string hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrEmpty(hostPath) && File.Exists(hostPath))
            {
                return hostPath;
            }

            // 2. DOTNET_ROOT — set by installers, ADO pipelines, and build scripts
            string dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(dotnetRoot))
            {
                string candidate = Path.Combine(dotnetRoot, dotnetExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // 3. Standard install locations (Windows: Program Files; Linux: /usr/share/dotnet, /usr/lib/dotnet)
            if (OperatingSystemHelper.IsWindowsOS)
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(programFiles))
                {
                    string candidate = Path.Combine(programFiles, "dotnet", dotnetExeName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            else
            {
                foreach (string linuxRoot in new[] { "/usr/share/dotnet", "/usr/lib/dotnet" })
                {
                    string candidate = Path.Combine(linuxRoot, dotnetExeName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            // 4. Fall back to hoping it's on PATH
            return dotnetExeName;
        }

        /// <summary>
        /// Captures a full process dump via <see cref="ProcessDumper"/>. This produces a much larger file
        /// than a .gcdump but does not require any external tools and can be analyzed with
        /// <c>dotnet-dump analyze</c>, WinDbg, or Visual Studio.
        /// </summary>
        private void CaptureFullDump(string timestamp)
        {
            string dumpFileName = $"BxlFullDump_{timestamp}.dmp";
            string dumpPath = Path.Combine(m_logsDirectory, dumpFileName);

            Tracing.Logger.Log.EngineDumpCollectorFailed(
                m_loggingContext,
                $"Bundled dotnet-gcdump failed, falling back to full process dump at {dumpPath}");

            try
            {
                using var self = Process.GetCurrentProcess();
                bool success = ProcessDumper.TryDumpProcess(self, dumpPath, out Exception dumpException, compress: false);

                if (success)
                {
                    var fileInfo = new FileInfo(dumpPath);
                    Tracing.Logger.Log.EngineDumpCollectorCompleted(m_loggingContext, $"{dumpPath} ({fileInfo.Length / (1024 * 1024)} MB)");
                }
                else
                {
                    Tracing.Logger.Log.EngineDumpCollectorFailed(
                        m_loggingContext,
                        $"Full process dump failed: {dumpException?.Message ?? "unknown error"}");
                }
            }
            catch (Exception ex)
            {
                Tracing.Logger.Log.EngineDumpCollectorFailed(m_loggingContext, $"Full process dump failed: {ex}");
            }
        }
    }
}

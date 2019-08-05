// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop.MacOS;
using BuildXL.Processes;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.CrashReporting;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.SandboxExec
{
    /// <summary>
    /// Executes a supplied executable and its arguments within the BuildXL sandbox and outputs all observed file accesses
    /// </summary>
    public class SandboxExecRunner : ISandboxedProcessFileStorage
    {
        private const string AccessOutput = "accesses.out";
        private static readonly double s_defaultProcessTimeOut = TimeSpan.FromMinutes(10).TotalSeconds;
        private static readonly double s_defaultProcessTimeOutMax = TimeSpan.FromHours(4).TotalSeconds;
        private static readonly LoggingContext s_loggingContext = new LoggingContext("BuildXL.SandboxExec");
        private static CrashCollectorMacOS s_crashCollector;

        /// <summary>
        /// Configuration options for this tool.
        /// </summary>
        public readonly struct Options
        {
            /// <summary>
            /// Defaults
            /// </summary>
            public static readonly Options Defaults =
                new Options(
                    verbose: false,
                    logToStdOut: false,
                    enableReportBatching: false,
                    reportQueueSizeMB: 1024,
                    enableTelemetry: true,
                    processTimeout: (int)s_defaultProcessTimeOut,
                    trackDirectoryCreation: false);

            /// <summary>
            /// When set to true, the output contains long instead of short description of reported accesses.
            /// </summary>
            public bool Verbose { get; }

            /// <summary>
            /// When set to true, the observed output goes to stdout, otherwise to AccessOutput
            /// </summary>
            public bool LogToStdOut { get; }

            /// <summary>
            /// Size of the kernel report queue in MB
            /// </summary>
            public uint ReportQueueSizeMB { get; }

            /// <summary>
            /// Tells the kext whether to batch reports or not.
            /// </summary>
            public bool EnableReportBatching { get; }

            /// <summary>
            /// When set to true, record statistics about execution time, deduping and send logs to remote telemetry.
            /// </summary>
            public bool EnableTelemetry { get; }

            /// <summary>
            /// Number of seconds before the parent process is timed out
            /// </summary>
            public int ProcessTimeout { get; }

            /// <summary>
            /// When set, directory creation is reported as "Write"; otherwise, it is reported as "Read"
            /// </summary>
            public bool TrackDirectoryCreation { get; }

            /// <nodoc />
            public Options(bool verbose, bool logToStdOut, uint reportQueueSizeMB, bool enableTelemetry, int processTimeout, bool trackDirectoryCreation, bool enableReportBatching)
            {
                Verbose = verbose;
                LogToStdOut = logToStdOut;
                ReportQueueSizeMB = reportQueueSizeMB;
                EnableReportBatching = enableReportBatching;
                EnableTelemetry = enableTelemetry;
                ProcessTimeout = processTimeout;
                TrackDirectoryCreation = trackDirectoryCreation;
            }
        }

        /// <summary>
        /// Builder for <see cref="Options"/>
        /// </summary>
        public class OptionsBuilder
        {
            /// <nodoc />
            public bool Verbose;

            /// <nodoc />
            public bool LogToStdOut;

            /// <nodoc />
            public uint ReportQueueSizeMB;

            /// <nodoc />
            public bool EnableReportBatching;

            /// <nodoc />
            public bool EnableTelemetry;

            /// <nodoc />
            public int ProcessTimeout;

            /// <nodoc />
            public bool TrackDirectoryCreation;

            /// <nodoc />
            public OptionsBuilder() { }

            /// <nodoc />
            public OptionsBuilder(Options opts)
            {
                Verbose = opts.Verbose;
                LogToStdOut = opts.LogToStdOut;
                ReportQueueSizeMB = opts.ReportQueueSizeMB;
                EnableReportBatching = opts.EnableReportBatching;
                EnableTelemetry = opts.EnableTelemetry;
                ProcessTimeout = opts.ProcessTimeout;
                TrackDirectoryCreation = opts.TrackDirectoryCreation;
            }

            /// <nodoc />
            public Options Finish() => new Options(Verbose, LogToStdOut, ReportQueueSizeMB, EnableTelemetry, ProcessTimeout, TrackDirectoryCreation, EnableReportBatching);
        }

        private readonly Options m_options;
        private readonly ISandboxConnection m_sandboxConnection;
        private PathTable m_pathTable;

        /// <summary>
        /// For unit tests only.
        /// </summary>
        public SandboxExecRunner(ISandboxConnection connection)
        {
            m_options = Options.Defaults;
            m_sandboxConnection = connection;
        }

        /// <nodoc />
        public SandboxExecRunner() : this(Options.Defaults) { }

        /// <nodoc />
        public SandboxExecRunner(Options options)
        {
            m_options = options;
            s_crashCollector = OperatingSystemHelper.IsUnixOS ? new CrashCollectorMacOS(new[] { CrashType.SandboxExec, CrashType.Kernel }) : null;
            m_sandboxConnection = OperatingSystemHelper.IsUnixOS
                ? 
#if PLATFORM_OSX
                OperatingSystemHelper.IsMacOSCatalinaOrHigher 
                    ? (ISandboxConnection) new SandboxConnectionES() 
                    : 
#endif                
                    (ISandboxConnection) new SandboxConnectionKext(
                        new SandboxConnectionKext.Config
                        {
                            FailureCallback = (int status, string description) =>
                            {
                                m_sandboxConnection.Dispose();
                                throw new SystemException($"Received unrecoverable error from the sandbox (Code: {status.ToString("X")}, Description: {description}), please reload the extension and retry.");
                            },
                            KextConfig = new Sandbox.KextConfig
                            {
                                ReportQueueSizeMB = m_options.ReportQueueSizeMB,
                                EnableReportBatching = m_options.EnableReportBatching,
#if PLATFORM_OSX
                                EnableCatalinaDataPartitionFiltering = OperatingSystemHelper.IsMacOSCatalinaOrHigher
#endif
                            },
                        })
                : null;
        }

        /// <summary>
        /// Splits given command line arguments into tool arguments and process arguments.
        ///
        /// Tool arguments are optional.  When provided, they must be specified first,
        /// then followed by "--" and then followed by process arguments.
        /// </summary>
        public static (Options toolOptions, string[] procArgs) ParseArgs(string[] args)
        {
            var separatorArgIdx = IndexOf(args, "--");
            var toolArgs = separatorArgIdx == -1 ? new string[0] : args.Take(separatorArgIdx).ToArray();
            var procArgs = args.Skip(separatorArgIdx + 1).ToArray();

            Options toolOptions = ParseOptions(toolArgs);
            return (toolOptions, procArgs);
        }

        /// <nodoc />
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                HandleUnhandledFailure(eventArgs.ExceptionObject as Exception);
            };

            var parsedArgs = ParseArgs(args);
            Environment.ExitCode = RunTool(parsedArgs.toolOptions, parsedArgs.procArgs);
        }

        private static void HandleUnhandledFailure(Exception exception)
        {
            PrintToStderr(exception.Message ?? exception.InnerException.Message);

            // Log the exception to telemetry
            Tracing.Logger.Log.SandboxExecCrashReport(s_loggingContext, s_loggingContext.Session.ToString(), exception.ToString());
            Telemetry.TelemetryShutdown();

            Environment.Exit(ExitCode.FromExitKind(ExitKind.InternalError));
        }

        private static int RunTool(Options options, string[] procArgs)
        {
            if (procArgs.Length < 1)
            {
                var macOSUsageDescription = OperatingSystemHelper.IsUnixOS ? $" [/{ArgReportQueueSizeMB}:<1-1024>] [/{ArgEnableReportBatching}[+,-]]" : "";
                PrintToStderr($"Usage: SandboxExec [[/{ArgVerbose}[+,-]] [/{ArgLogToStdOut}[+,-]] [/{ArgProcessTimeout}:seconds] [/{ArgTrackDirectoryCreation}] [/{ArgEnableStatistics}[+,-]]{macOSUsageDescription} --] executable [arg1 arg2 ...]");
                return 1;
            }

            if (!Path.IsPathRooted(procArgs[0]))
            {
                PrintToStderr("The path to the executable must be specified as an absolute path. Exiting.");
                return 1;
            }

            Telemetry.TelemetryStartup(options.EnableTelemetry);
            CleanupOutputs();

            var instance = new SandboxExecRunner(options);

            SandboxedProcessResult result;
            Stopwatch sandboxingTime;
            var overallTime = Stopwatch.StartNew();
            using (var process = ExecuteAsync(instance, procArgs, workingDirectory: Directory.GetCurrentDirectory()).GetAwaiter().GetResult())
            {
                sandboxingTime = Stopwatch.StartNew();
                result = process.GetResultAsync().GetAwaiter().GetResult();
                sandboxingTime.Stop();

                PrintToStdout($"Process {procArgs[0]}:{process.ProcessId} exited with exit code: {result.ExitCode}");
            }

            var dedupeTime = Stopwatch.StartNew();

            var accessReportCount = result.FileAccesses.Count;

            // Dedupe reported file accesses
            var distinctAccessReports = instance.DedupeAccessReports(
                result.FileAccesses,
                result.ExplicitlyReportedFileAccesses,
                result.AllUnexpectedFileAccesses);

            dedupeTime.Stop();

            var outputTime = Stopwatch.StartNew();
            if (options.LogToStdOut)
            {
                foreach (var report in distinctAccessReports)
                {
                    PrintToStdout(report);
                }
            }
            else
            {
                var path = Path.Combine(Directory.GetCurrentDirectory(), AccessOutput);
                try
                {
                    File.WriteAllLines(path, distinctAccessReports);
                }
                catch (IOException ex)
                {
                    PrintToStderr($"Could not write file access report file to {path}. Got excecption instead: " + ex.ToString());
                }
            }
            outputTime.Stop();

            var disposeTime = Stopwatch.StartNew();
            if (instance.m_sandboxConnection != null)
            {
                // Take care of releasing sandbox kernel extension resources on macOS
                instance.m_sandboxConnection.Dispose();
            }
            disposeTime.Stop();

            overallTime.Stop();

            if (options.EnableTelemetry)
            {
                PrintToStdout("\nTime unit is: ms\n");
                PrintToStdout($"Overall execution time:                       {overallTime.ElapsedMilliseconds}");
                PrintToStdout($"Time spent executing process with sandboxing: {sandboxingTime.ElapsedMilliseconds}");
                PrintToStdout($"Time spent deduping access reports:           {dedupeTime.ElapsedMilliseconds}");
                PrintToStdout($"Time spent outputting access reports:         {outputTime.ElapsedMilliseconds}");
                PrintToStdout($"Time spent disposing kernel connection:       {disposeTime.ElapsedMilliseconds}");
                PrintToStdout($"Number of access reports before deduping:     {accessReportCount}");
                PrintToStdout($"Number of access reports after deduping:      {distinctAccessReports.Count}");

                PrintToStdout("");
                PrintToStdout("Statistics ::");
                foreach (KeyValuePair<string, long> kvp in SandboxedProcessFactory.Counters.AsStatistics())
                {
                    PrintToStdout($"{kvp.Key} = {kvp.Value}");
                }
                PrintToStdout("");
            }

            CollectAndUploadCrashReports(options.EnableTelemetry);
            Telemetry.TelemetryShutdown();

            return result.ExitCode;
        }

        /// <summary>
        /// Dedupes a variable list of reported file accesses and returns the result as an enumerable collection of strings containing
        /// the short description of all the input file access reports
        /// </summary>
        /// <param name="accessReports">Variable number of sets of reported file accesses</param>
        /// <returns>A deduped List of strings containing the short description of all file access reports</returns>
        public List<string> DedupeAccessReports(params ISet<ReportedFileAccess>[] accessReports)
        {
            return accessReports
                .SelectMany(set => set.Select(RenderReport))
                .Distinct()
                .ToList();
        }

        private static long s_pipIdCounter = 1;

        /// <summary>
        /// Creates a SandboxedProcessInfo object that can be used to run a target program within the BuildXL Sandbox and
        /// forces the Sandbox to explicitly report all file accesses observed
        /// </summary>
        /// <param name="processFileName">The fully qualifying file name of the process to run inside the sandbox</param>
        /// <param name="instance">A SandboxExecRunner instance</param>
        /// <returns>SandboxedProcessInfo object that is configured to explicitly report all observed file accesses</returns>
        public static SandboxedProcessInfo CreateSandboxedProcessInfo(string processFileName, SandboxExecRunner instance)
        {
            var sandboxProcessInfo = new SandboxedProcessInfo(fileStorage: instance, fileName: processFileName, disableConHostSharing: true);
            sandboxProcessInfo.PipDescription = processFileName;
            sandboxProcessInfo.SandboxConnection = instance.m_sandboxConnection;

            sandboxProcessInfo.StandardOutputEncoding = Encoding.UTF8;
            sandboxProcessInfo.StandardOutputObserver = PrintToStdout;

            sandboxProcessInfo.StandardErrorEncoding = Encoding.UTF8;
            sandboxProcessInfo.StandardErrorObserver = PrintToStderr;

            // track directory creation
            sandboxProcessInfo.FileAccessManifest.EnforceAccessPoliciesOnDirectoryCreation = instance.m_options.TrackDirectoryCreation;

            // Enable explicit file access reporting
            sandboxProcessInfo.FileAccessManifest.ReportFileAccesses = true;
            sandboxProcessInfo.FileAccessManifest.FailUnexpectedFileAccesses = false;
            sandboxProcessInfo.FileAccessManifest.PipId = Interlocked.Increment(ref s_pipIdCounter);
            return sandboxProcessInfo;
        }

        /// <summary>
        /// Runs a target process inside the sandbox asynchronously
        /// </summary>
        /// <param name="instance">A SandboxExecRunner instance</param>
        /// <param name="exec">The programs full command line including its arguments</param>
        /// <param name="workingDirectory">Working directory in which to start the process</param>
        /// <returns>Task that will execute the process and its arguments inside the BuildXL Sandbox</returns>
        public static Task<ISandboxedProcess> ExecuteAsync(SandboxExecRunner instance, string[] exec, string workingDirectory)
        {
            var processFileName = exec[0];
            var processInfo = CreateSandboxedProcessInfo(processFileName, instance);
            processInfo.Timeout = TimeSpan.FromSeconds(instance.m_options.ProcessTimeout);
            processInfo.Arguments = ExtractAndEscapeCommandLineArguments(exec);
            processInfo.WorkingDirectory = workingDirectory;
            processInfo.EnvironmentVariables = BuildParameters.GetFactory(null).PopulateFromEnvironment();
            instance.m_pathTable = processInfo.PathTable;

            return SandboxedProcessFactory.StartAsync(processInfo, forceSandboxing: true);
        }

        /// <summary>
        /// Sanitizes and returns the process command line arguments
        /// </summary>
        /// <param name="exec">The complete process command line list</param>
        /// <returns>Space separated execution arguments for the process to be run inside the Sandbox, escaped with single quotes</returns>
        public static string ExtractAndEscapeCommandLineArguments(string[] exec)
        {
            var args = exec.Skip(1);
            // Take care of escaping process arguments on Unix systems via wrapping them in single quotation marks and escpaing existing single quotes to maintain original intent
            return string.Join(" ", OperatingSystemHelper.IsUnixOS ? args.Select(s => "'" + s.Replace("'", "'\\''") + "'") : args);
        }

        /// <summary>
        /// Deletes report files from previous runs if they exist
        /// </summary>
        private static void CleanupOutputs()
        {
            // SandboxExec redirects stdout or stderr output to a file for every run as defined in SandboxedProcessFile (out.txt and err.txt repsectively)
            // Clean up any files from previous runs if they exist so the new output contains only information of the current run
            var stdout = Path.Combine(Directory.GetCurrentDirectory(), SandboxedProcessFile.StandardOutput.DefaultFileName());
            var stderr = Path.Combine(Directory.GetCurrentDirectory(), SandboxedProcessFile.StandardError.DefaultFileName());

            try
            {
                File.Delete(stdout);
                File.Delete(stderr);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <nodoc />
        public string GetFileName(SandboxedProcessFile file)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), file.DefaultFileName());
        }

        private static int IndexOf(string[] arr, string elem)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].Equals(elem, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private const string ArgVerbose = "verbose";
        private const string ArgLogToStdOut = "logToStdOut";
        private const string ArgReportQueueSizeMB = "reportQueueSizeMB";
        private const string ArgEnableReportBatching = "enableReportBatching";
        private const string ArgEnableStatistics = "enableStatistics";
        private const string ArgProcessTimeout = "processTimeout";
        private const string ArgTrackDirectoryCreation = "trackDirectoryCreation";

        private static Options ParseOptions(string[] toolArgs)
        {
            var opts = new OptionsBuilder(Options.Defaults);

            var cli = new CommandLineUtilities(toolArgs);
            foreach (var opt in cli.Options)
            {
                switch (opt.Name.TrimEnd('-', '+'))
                {
                    case ArgVerbose:
                    case "v":
                        opts.Verbose = CommandLineUtilities.ParseBooleanOption(opt);
                        break;
                    case ArgLogToStdOut:
                    case "o":
                        opts.LogToStdOut = CommandLineUtilities.ParseBooleanOption(opt);
                        break;
                    case "numKextConnections":
                    case "c":
                        Console.WriteLine($"*** WARNING *** option /{opt.Name} has no effect any longer");
                        break;
                    case ArgReportQueueSizeMB:
                    case "r":
                        opts.ReportQueueSizeMB = CommandLineUtilities.ParseUInt32Option(opt, 1, 1024);
                        break;
                    case ArgEnableReportBatching:
                    case "b":
                        opts.EnableReportBatching = CommandLineUtilities.ParseBooleanOption(opt);
                        break;
                    case ArgEnableStatistics:
                    case "s":
                        opts.EnableTelemetry = CommandLineUtilities.ParseBooleanOption(opt);
                        break;
                    case ArgProcessTimeout:
                    case "t":
                        // Max is currently set to 4 hours and should suffice
                        opts.ProcessTimeout = CommandLineUtilities.ParseInt32Option(opt, (int)s_defaultProcessTimeOut, (int)s_defaultProcessTimeOutMax);
                        break;
                    case ArgTrackDirectoryCreation:
                    case "d":
                        opts.TrackDirectoryCreation = CommandLineUtilities.ParseBooleanOption(opt);
                        break;
                    default:
                        throw new InvalidArgumentException($"Unrecognized option {opt.Name}");
                }
            }

            return opts.Finish();
        }

        private static void PrintToStdout(string message)
        {
            Console.WriteLine(message);
        }

        private static void PrintToStderr(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(message);
            Console.ResetColor();
        }

        private string RenderReport(ReportedFileAccess report)
        {
            return m_options.Verbose
                ? $"Path = {report.GetPath(m_pathTable)}{Environment.NewLine}{report.Describe()}"
                : report.ShortDescribe(m_pathTable);
        }

        private static void CollectAndUploadCrashReports(bool remoteTelemetryEnabled)
        {
            if (s_crashCollector != null)
            {
                // Put the state file at the root of the sandbox exec directory
                var stateFileDirectory = Directory.GetCurrentDirectory();

                CrashCollectorMacOS.Upload upload = (IReadOnlyList<CrashReport> reports, string sessionId) =>
                {
                    if (!remoteTelemetryEnabled)
                    {
                        return false;
                    }

                    foreach (var report in reports)
                    {
                        Tracing.Logger.Log.DominoMacOSCrashReport(s_loggingContext, sessionId, report.Content, report.Type.ToString(), report.FileName);
                    }

                    return true;
                };

                try
                {
                    s_crashCollector.UploadCrashReportsFromLastSession(s_loggingContext.Session.Id, stateFileDirectory, out var stateFilePath, upload);
                }
                catch (Exception exception)
                {
                    PrintToStderr(exception.Message ?? exception.InnerException.Message);
                }
            }
        }
    }
}

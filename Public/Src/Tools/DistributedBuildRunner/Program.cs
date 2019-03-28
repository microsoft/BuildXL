// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Processes;
using Microsoft.Win32.SafeHandles;

namespace Tool.DistributedBuildRunner
{
    /// <summary>
    /// Simple process used by TestProjectGenerator-generated projects to product arbitrarily large outputs.
    /// </summary>
    public static class Program
    {
        private const int SuccessExitCode = 0;
        private const int ErrorExitCode = 1;

        private static object s_processCreationLock = new object();
        private static bool s_ignoreErrorMessages = false;
        private static bool s_killSurvivingProcessesAfterTimeout = true;

        /// <param name="args">
        /// args[0] - number of workers: the number of workers (in addition to master) to use for the build.
        /// args[1] - add distribution parameters: (true or false). Indicates whether to add standard distribution parameters.
        /// </param>
        public static int Main(string[] args)
        {
            if (Environment.GetEnvironmentVariable("SmdbDebugOnStart") == "1")
            {
                Debugger.Launch();
            }

            // Ensure that any launched processes will close when this process closes.
            if (JobObject.OSSupportsNestedJobs)
            {
                JobObject.SetTerminateOnCloseOnCurrentProcessJob();
            }

            if (args.Length < 1 || args.Length > 2)
            {
                Console.WriteLine(Resources.HelpText);

                if (args.Length != 0)
                {
                    Console.Error.WriteLine(Resources.Error_UnexpectedArgumentCount);
                }

                return ErrorExitCode;
            }

            uint numberOfWorkers = 0;
            int crashWorkerId = -1;

            if (!ParseArguments(args, ref numberOfWorkers, ref crashWorkerId))
            {
                return ErrorExitCode;
            }

            bool success = true;
            string exePath;
            List<string> commandLines;
            if (!ConstructCommandLines(numberOfWorkers, out exePath, out commandLines))
            {
                return ErrorExitCode;
            }

            List<Task<bool>> tasks = new List<Task<bool>>();

            var messages = new BlockingCollection<ConsoleMessage>();

            var messageTask = Task.Factory.StartNew(() => WriteProcessConsoles(messages), TaskCreationOptions.LongRunning);

            for (int i = 0; i < commandLines.Count; i++)
            {
                int workerId = i;
                var commandLine = commandLines[i];

                messages.Add(new ConsoleMessage()
                {
                    WorkerId = workerId,
                    Text = string.Format(CultureInfo.CurrentCulture, Resources.WorkerRunStartMessage, exePath, commandLine),
                });
            }

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            for (int i = 0; i < commandLines.Count; i++)
            {
                int workerId = i;
                var commandLine = commandLines[i];
                tasks.Add(Task.Factory.StartNew(
                    () => RunProcess(exePath, commandLine, messages, workerId, cancellationTokenSource, workerId == crashWorkerId),
                    TaskCreationOptions.LongRunning));
            }

            success &= Task.WhenAll(tasks.ToArray()).Result.All(succeeded => succeeded);

            messages.CompleteAdding();
            success &= messageTask.Result;

            return success ? SuccessExitCode : ErrorExitCode;
        }

        private static ushort GetPortNumber()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return (ushort)port;
        }

        private static bool ConstructCommandLines(uint numberOfWorkers, out string exePath, out List<string> commandLines)
        {
            exePath = Environment.GetEnvironmentVariable("BUILDXL_EXE_PATH");
            commandLines = null;

            string cacheConfigTemplatePath = Environment.GetEnvironmentVariable("SMDB.CACHE_CONFIG_TEMPLATE_PATH");
            string cacheConfigOutputPath = Environment.GetEnvironmentVariable("SMDB.CACHE_CONFIG_OUTPUT_PATH");
            string cacheTemplatePath = Environment.GetEnvironmentVariable("SMDB.CACHE_TEMPLATE_PATH");
            string commonArgs = Environment.GetEnvironmentVariable("BUILDXL_COMMON_ARGS");
            string workerArgs = Environment.GetEnvironmentVariable("BUILDXL_WORKER_ARGS");
            string masterArgs = Environment.GetEnvironmentVariable("BUILDXL_MASTER_ARGS");
            s_killSurvivingProcessesAfterTimeout = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_DBD_TEST_KILL"));

            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                Console.Error.WriteLine($"BuildXL executable does not exist at file path: '{exePath}'");
                return false;
            }

            if (string.IsNullOrEmpty(cacheConfigTemplatePath) || !File.Exists(cacheConfigTemplatePath))
            {
                Console.Error.WriteLine($"Cache config template does not exist at file path: '{cacheConfigTemplatePath ?? string.Empty}'");
                return false;
            }

            var cacheConfigTemplate = File.ReadAllText(cacheConfigTemplatePath);

            commandLines = new List<string>();
            StringBuilder commandLineBuilder = new StringBuilder();

            var exeRootLetter = Path.GetFullPath(exePath)[0];

            string sourceRootLetter = GetRootLetter(exeRootLetter, 'S', 'T');
            string objectRootLetter = GetRootLetter(exeRootLetter, 'O', 'P');
            var masterCacheRoot = ReplaceArgs(cacheTemplatePath, 0, sourceRootLetter, objectRootLetter, masterCacheRoot: null);

            for (int i = 0; i <= numberOfWorkers; i++)
            {
                var cacheConfig = ReplaceArgs(cacheConfigTemplate, i, sourceRootLetter, objectRootLetter, masterCacheRoot);

                var machineCacheConfigOutputPath = ReplaceArgs(cacheConfigOutputPath, i, sourceRootLetter, objectRootLetter, masterCacheRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(machineCacheConfigOutputPath));
                File.WriteAllText(machineCacheConfigOutputPath, cacheConfig);
            }

            // Construct the master command line
            InitializeArgs(commandLineBuilder, commonArgs + " " + masterArgs, 0, sourceRootLetter, objectRootLetter, masterCacheRoot);
            commandLineBuilder.AppendArg("/distributedBuildRole:master");

            var masterPort = GetPortNumber();
            var workerPorts = new List<int>();
            commandLineBuilder.AppendArg("/distributedBuildServicePort:{0}", masterPort);

            for (int i = 0; i < numberOfWorkers; i++)
            {
                int workerPort = GetPortNumber();
                workerPorts.Add(workerPort);
                commandLineBuilder.AppendArg("/distributedBuildWorker:{0}:{1}", "localhost", workerPort);
            }

            commandLines.Add(commandLineBuilder.ToString());

            // Construct the worker command lines
            for (int i = 0; i < numberOfWorkers; i++)
            {
                InitializeArgs(
                    commandLineBuilder,
                    commonArgs + " " + workerArgs,
                    i + 1,
                    sourceRootLetter,
                    objectRootLetter,
                    masterCacheRoot);

                commandLineBuilder.AppendArg("/distributedBuildRole:worker");
                commandLineBuilder.AppendArg("/distributedBuildServicePort:{0}", workerPorts[i]);

                commandLines.Add(commandLineBuilder.ToString());
            }

            return true;
        }

        private static bool ParseArguments(string[] args, ref uint numberOfWorkers, ref int crashWorker)
        {
            if (!uint.TryParse(args[0], out numberOfWorkers) || numberOfWorkers > 100)
            {
                Console.Error.WriteLine(Resources.Error_ParsingNumberOfWorkers, args[0]);
                return false;
            }

            if (args.Length > 1 && !int.TryParse(args[1], out crashWorker))
            {
                Console.Error.WriteLine(Resources.Error_ParsingCrashWorker, args[1], numberOfWorkers - 1);
                return false;
            }

            return true;
        }

        private static bool WriteProcessConsoles(BlockingCollection<ConsoleMessage> messages)
        {
            bool success = true;

            foreach (var message in messages.GetConsumingEnumerable())
            {
                if (message.Text == null)
                {
                    continue;
                }

                var consoleColor = Console.ForegroundColor;
                if (message.Type == MessageType.Error)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    if (!s_ignoreErrorMessages)
                    {
                        success = false;
                    }
                }

                if (message.Type == MessageType.Warning)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }

                Console.WriteLine($"M{message.WorkerId:D2}: {message.Text}");

                if (message.Type != MessageType.Info)
                {
                    Console.ForegroundColor = consoleColor;
                }
            }

            return success;
        }

        private static bool RunProcess(string process, string commandLine, BlockingCollection<ConsoleMessage> messages, int workerId,
                                       CancellationTokenSource cancellationTokenSource, bool crashWorker)
        {
            var processStartInfo = new ProcessStartInfo(process, commandLine)
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

            string workerIdentifier = string.Format(CultureInfo.InvariantCulture, "BUILDXL_M{0:D2}_", workerId).ToUpperInvariant();

            foreach (DictionaryEntry envVar in Environment.GetEnvironmentVariables())
            {
                string key = (string)envVar.Key;
                string value = (string)envVar.Value;
                if (key.ToUpperInvariant().StartsWith(workerIdentifier))
                {
                    key = key.Remove(0, workerIdentifier.Length);
                    processStartInfo.EnvironmentVariables.Add(key, value);
                }
            }

            Process p = Process.Start(processStartInfo);

            // Capture output of the process
            p.OutputDataReceived += (sender, e) =>
                                    {
                                        messages.Add(
                                            new ConsoleMessage
                                            {
                                                Type = (e.Data != null && e.Data.Contains("warning DX")) ? MessageType.Warning : MessageType.Info,
                                                WorkerId = workerId,
                                                Text = e.Data,
                                            });

                                        if (crashWorker && e.Data != null && e.Data.Contains("Pips:") && !e.Data.Contains("0 running"))
                                        {
                                            // After the kill some pips are supposed to fail. They should not make the test utility return a failure.
                                            s_ignoreErrorMessages = true;

                                            p.Kill();
                                            messages.Add(
                                                new ConsoleMessage
                                                {
                                                    Type = MessageType.Error,
                                                    WorkerId = workerId,
                                                    Text = Resources.Message_KillingProcess,
                                                });
                                        }
                                    };

            bool loggedError = false;

            p.ErrorDataReceived += (sender, e) =>
                                   {
                                       if (e.Data != null)
                                       {
                                           messages.Add(
                                               new ConsoleMessage
                                               {
                                                   Type = MessageType.Error,
                                                   WorkerId = workerId,
                                                   Text = e.Data,
                                               });

                                           loggedError = true;
                                       }
                                   };

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            var processWaitHandle = new ManualResetEvent(true);
            processWaitHandle.SafeWaitHandle = new SafeWaitHandle(p.Handle, false);

            WaitHandle.WaitAny(new[] { processWaitHandle, cancellationTokenSource.Token.WaitHandle });

            // See if process exited
            if (p.WaitForExit(0))
            {
                // Check that there was no error
                // A non-zero exit code is not considered a failure when it is reported by the crashed worker
                // or by the master in the crash test.
                bool isErrorExitCode = p.ExitCode != 0 && (!s_ignoreErrorMessages || (workerId != 0 && !crashWorker));

                messages.Add(new ConsoleMessage
                {
                    Type = isErrorExitCode ? MessageType.Error : MessageType.Info,
                    WorkerId = workerId,
                    Text = string.Format(CultureInfo.CurrentCulture, Resources.ProcessExitCode, p.ExitCode),
                });

                if (isErrorExitCode)
                {
                    loggedError = true;
                }
            }
            else
            {
                p.Kill();

                // If process did not exit, error that process did not exit within timeout period
                // of other process exiting
                messages.Add(new ConsoleMessage()
                {
                    WorkerId = workerId,
                    Text = Resources.Error_TimedOutWaitingForExit,
                });

                loggedError = true;
            }

            // If any of the processes exits, wait for some amount of time then cancel waiting on other processes
            // If we are the ones killing the process, do not kill others
            if (!crashWorker && s_killSurvivingProcessesAfterTimeout)
            {
                cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(30));
            }

            return !loggedError;
        }

        private static string GetRootLetter(char exeRootLetter, char defaultRootLetter, char alternateRootLetter)
        {
            return char.ToUpper(exeRootLetter) == char.ToUpper(defaultRootLetter) ? alternateRootLetter.ToString() : defaultRootLetter.ToString();
        }

        /// <summary>
        /// Adds the common arguments performing necessary token replacements
        /// </summary>
        public static void InitializeArgs(
            this StringBuilder builder,
            string commonArgs,
            int machineNumber,
            string sourceRootLetter,
            string objectRootLetter,
            string masterCacheRoot)
        {
            builder.Clear();

            commonArgs = ReplaceArgs(commonArgs, machineNumber, sourceRootLetter, objectRootLetter, masterCacheRoot);
            builder.AppendArg(commonArgs);
        }

        private static string ReplaceArgs(string value, int machineNumber, string sourceRootLetter, string objectRootLetter, string masterCacheRoot)
        {
            value = value.ReplaceIgnoreCase("{masterMachineNumber}", "M00");
            value = value.ReplaceIgnoreCase("{machineNumber}", machineNumber.ToString().PadLeft(2, '0'));
            return ReplaceArgs(value, sourceRootLetter, objectRootLetter, masterCacheRoot);
        }

        private static string ReplaceArgs(string value, string sourceRootLetter, string objectRootLetter, string masterCacheRoot)
        {
            value = value.ReplaceIgnoreCase("{masterCacheRoot}", masterCacheRoot);
            value = value.ReplaceIgnoreCase("{escapedMasterCacheRoot}", masterCacheRoot?.Replace(@"\", @"\\"));
            value = value.ReplaceIgnoreCase("{sourceRoot}", sourceRootLetter);
            value = value.ReplaceIgnoreCase("{objectRoot}", objectRootLetter);
            return value;
        }

        public static string ReplaceIgnoreCase(this string input, string oldValue, string newValue)
        {
            if (newValue == null)
            {
                return input;
            }

            oldValue = Regex.Escape(oldValue);
            return Regex.Replace(input, oldValue, newValue, RegexOptions.IgnoreCase);
        }

        public static void AppendArg(this StringBuilder builder, string argument, params object[] args)
        {
            builder.Append(" ");
            builder.AppendFormat(argument, args);
        }

        public static void AppendArg(this StringBuilder builder, string argument)
        {
            builder.Append(" ");
            builder.Append(argument);
        }

        private enum MessageType
        {
            Info,
            Warning,
            Error,
        }

        private class ConsoleMessage
        {
            public MessageType Type;
            public string Text;
            public int WorkerId;
        }
    }
}

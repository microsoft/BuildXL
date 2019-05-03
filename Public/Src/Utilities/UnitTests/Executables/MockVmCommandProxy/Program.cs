// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BuildXL.Utilities;
using Newtonsoft.Json;

namespace Test.BuildXL.Executables.MockVmCommandProxy
{
    /// <summary>
    /// Test process that performs filesystem operations
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Runs the process executing filesystem operations specified by command line args
        /// </summary>
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Command is not specified");
                return -1;
            }

            const string StartBuildCmd = "StartBuild";
            const string RunCmd = "Run";


            string inputFile = null;
            string outputFile = null;
            string command = args[0];

            if (string.Equals(StartBuildCmd, command, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseArgs(args, out inputFile, out _))
                {
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(inputFile))
                {
                    Console.Error.WriteLine($"{StartBuildCmd} command requires input");
                    return -1;
                }
            }
            else if (string.Equals(RunCmd, command, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseArgs(args, out inputFile, out outputFile))
                {
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(inputFile) || string.IsNullOrWhiteSpace(outputFile))
                {
                    Console.Error.WriteLine($"{RunCmd} command requires input and output");
                    return -1;
                }
            }
            else
            {
                Console.Error.WriteLine("Unknown command");
                return -1;
            }

            return 0;
        }

        private static bool TryParseArgs(string[] args, out string inputFile, out string outputFile)
        {
            inputFile = null;
            outputFile = null;
            const string InputFileArgPrefix = "/InputJsonFile:";
            const string OutputFileArgPrefix = "/OutputJsonFile:";

            for (int i = 1; i < args.Length; ++i)
            {
                if (args[i].StartsWith(InputFileArgPrefix))
                {
                    inputFile = args[i].Substring(InputFileArgPrefix.Length);
                }
                if (args[i].StartsWith(OutputFileArgPrefix))
                {
                    outputFile = args[i].Substring(OutputFileArgPrefix.Length);
                }
                else
                {
                    Console.Error.WriteLine($"Unknown param '{args[i]}'");
                    return false;
                }
            }

            return true;
        }

        private static int StartBuild(string inputFile)
        {
            StartBuildRequest request = JsonConvert.DeserializeObject<StartBuildRequest>(File.ReadAllText(inputFile));

            Console.WriteLine($"HostLowPrivilegeUsername: {request.HostLowPrivilegeUsername ?? string.Empty}");
            Console.WriteLine($"HostLowPrivilegePassword: {request.HostLowPrivilegePassword ?? string.Empty}");

            return 0;
        }

        private static int Run(string inputFile, string outputFile)
        {
            RunRequest request = JsonConvert.DeserializeObject<RunRequest>(File.ReadAllText(inputFile));

            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();

            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = request.AbsolutePath,
                    Arguments = request.Arguments,
                    WorkingDirectory = request.WorkingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },

                EnableRaisingEvents = true
            })
            {

                process.OutputDataReceived += (s, e) => stdOut.AppendLine(e.Data);
                process.ErrorDataReceived += (s, e) => stdErr.AppendLine(e.Data);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                var stdOutPath = Path.Combine(request.WorkingDirectory, "vm.std.out");
                var stdErrPath = Path.Combine(request.WorkingDirectory, "vm.std.err");
                File.WriteAllText(stdOutPath, stdOut.ToString());
                File.WriteAllText(stdErrPath, stdErr.ToString());

                var result = new RunResult
                {
                    StdOut = stdOut.ToString(),
                    StdErr = stdErr.ToString(),
                    ProcessStateInfo = new ProcessStateInfo
                    {
                        StdOutPath = stdOutPath,
                        StdErrPath = stdErrPath,
                        ExitCode = process.ExitCode,
                        ProcessState = process.HasExited ? ProcessState.Exited : ProcessState.Unknown,
                        TerminationReason = ProcessTerminationReason.None
                    }
                };

                var jsonSerializer = JsonSerializer.Create(new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Include
                });

                Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

                using (var streamWriter = new StreamWriter(outputFile))
                using (var jsonTextWriter = new JsonTextWriter(streamWriter))
                {
                    jsonSerializer.Serialize(jsonTextWriter, result);
                }

                return 0;
            }
        }
    }
}

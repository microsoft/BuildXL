// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BuildXL.Utilities;
using BuildXL.Utilities.VmCommandProxy;

namespace Test.BuildXL.Executables.MockVmCommandProxy
{
    /// <summary>
    /// Mock of VmCommandProxy for testing.
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

            string inputFile = null;
            string outputFile = null;
            string command = args[0];

            if (string.Equals(VmCommands.InitializeVm, command, StringComparison.OrdinalIgnoreCase))
            {
                return InitializeVm();
            }
            else if (string.Equals(VmCommands.Run, command, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseArgs(args, out inputFile, out outputFile))
                {
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(inputFile) || string.IsNullOrWhiteSpace(outputFile))
                {
                    Console.Error.WriteLine($"{VmCommands.Run} command requires input and output");
                    return -1;
                }

                return Run(inputFile, outputFile);
            }
            else
            {
                Console.Error.WriteLine("Unknown command");
                return -1;
            }
        }

        private static bool TryParseArgs(string[] args, out string inputFile, out string outputFile)
        {
            inputFile = null;
            outputFile = null;
            string inputFileArgPrefix = $"/{VmCommands.Params.InputJsonFile}:";
            string outputFileArgPrefix = $"/{VmCommands.Params.OutputJsonFile}:";

            for (int i = 1; i < args.Length; ++i)
            {
                if (args[i].StartsWith(inputFileArgPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    inputFile = args[i].Substring(inputFileArgPrefix.Length);
                }
                else if (args[i].StartsWith(outputFileArgPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    outputFile = args[i].Substring(outputFileArgPrefix.Length);
                }
                else
                {
                    Console.Error.WriteLine($"Unknown param '{args[i]}'");
                    return false;
                }
            }

            return true;
        }

        private static int InitializeVm() => 0;

        private static int Run(string inputFile, string outputFile)
        {
            Console.WriteLine($"Read request from '{inputFile}'");

            RunRequest request = VmSerializer.DeserializeFromFile<RunRequest>(inputFile);
            
            Console.WriteLine($"Run request '{request.AbsolutePath} {request.Arguments}'");

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

                Console.WriteLine($"Finish request '{request.AbsolutePath} {request.Arguments}'");

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

                Console.WriteLine($"Write result to '{outputFile}'");

                VmSerializer.SerializeToFile(outputFile, result);

                return 0;
            }
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BuildXL.Demo;
using BuildXL.Processes;

namespace BuildXL.SandboxDemo
{
    /// <summary>
    /// A process is run under the sandbox and the process tree is reported
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Expected arguments: 
        /// - args[0]: process to run under the sandbox
        /// - args[1..n]: optional arguments that are passed to the process 'as is'
        /// </summary>
        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                PrintUsage();
                return 1;
            }

            var tool = args[0];
            var arguments = string.Join(" ", args.Skip(1));

            var processReporter = new ProcessReporter();
            var reportedProcesses = processReporter.RunProcessAndReportTree(tool, arguments);

            PrintProcesses(tool, reportedProcesses);

            return 0;
        }

        private static void PrintProcesses(string tool, IReadOnlyList<ReportedProcess> reportedProcesses)
        {
            Console.WriteLine($"Process '{tool}' ran under the sandbox. These processes were launched in the sandbox:");
            foreach (var reportedProcess in reportedProcesses)
            {
                Console.WriteLine($"{string.Empty}{reportedProcess.Path} [ran {(reportedProcess.ExitTime - reportedProcess.CreationTime).TotalMilliseconds}ms]");
            }
        }

        private static void PrintUsage()
        {
            var processName = Process.GetCurrentProcess().ProcessName;
            Console.WriteLine($"{processName} <pathToTool> [<arguments>]");
        }
    }
}
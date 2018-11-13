// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Linq;
using BuildXL.Demo;

namespace BuildXL.SandboxDemo
{
    /// <summary>
    /// An arbitrary process can be run under the BuildXL Sandbox and all files accesses of itself and its 
    /// child processes are reported
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Expected arguments: 
        /// - args[0]: path to the process to be executed under the sandbox
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

            var fileAccessReporter = new FileAccessReporter();
            var result = fileAccessReporter.RunProcessUnderSandbox(tool, arguments).GetAwaiter().GetResult();

            var accessesByPath = result
                .FileAccesses
                .Select(access => access.GetPath(fileAccessReporter.PathTable))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var displayAccesses = string.Join(Environment.NewLine, accessesByPath);

            Console.WriteLine($"Process '{tool}' ran under BuildXL sandbox with arguments '{arguments}' and returned with exit code '{result.ExitCode}'. Sandbox reports {result.FileAccesses.Count} file accesses:");
            Console.WriteLine(displayAccesses);

            return 0;
        }

        private static void PrintUsage()
        {
            var processName = Process.GetCurrentProcess().ProcessName;
            Console.WriteLine($"{processName} <pathToTool> [<arguments>]");
        }
    }
}
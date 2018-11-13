// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Linq;
using BuildXL.Demo;

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

            var processTreeBuilder = new ProcessTreeBuilder();
            var processTree = processTreeBuilder.RunProcessAndReportTree(tool, arguments);

            PrintTree(tool, processTree);

            return 0;
        }

        private static void PrintTree(string tool, ProcessTree processTree)
        {
            Console.WriteLine($"Process '{tool}' ran under the sandbox. The process tree is the following:");
            PrintNode(string.Empty, processTree.Root);
        }

        private static void PrintNode(string indent, ProcessNode processNode)
        {
            var reportedProcess = processNode.ReportedProcess;
            Console.WriteLine($"{indent}{reportedProcess.Path} [ran {(reportedProcess.ExitTime - reportedProcess.CreationTime).TotalMilliseconds}ms]");
            foreach (var childrenProcess in processNode.Children)
            {
                var newIndent = new string(' ', indent.Length) + (childrenProcess == processNode.Children.Last() ? "└──" : "├──");
                PrintNode(newIndent, childrenProcess);
            }
        }

        private static void PrintUsage()
        {
            var processName = Process.GetCurrentProcess().ProcessName;
            Console.WriteLine($"{processName} <pathToTool> [<arguments>]");
        }
    }
}
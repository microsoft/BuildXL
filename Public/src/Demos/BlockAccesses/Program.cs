// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BuildXL.Demo;
using BuildXL.Processes;
using BuildXL.Utilities;

namespace BuildXL.SandboxDemo
{
    /// <summary>
    /// A given directory is enumerated under the sandbox, where some directory can be blocked
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Expected arguments: 
        /// - args[0]: directory to enumerate recursively
        /// - args[1..n]: optional paths representing directories to recursively block accesses from
        /// </summary>
        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                PrintUsage();
                return 1;
            }

            var pathTable = new PathTable();

            if (!AbsolutePath.TryCreate(pathTable, args[0], out AbsolutePath directoryToEnumerate))
            {
                Console.Error.WriteLine($"Could not parse the directory to enumerate '{args[0]}' as a path.");
                return 1;
            }

            var directoriesToBlock = new List<AbsolutePath>(args.Length);
            foreach (var argument in args.Skip(1))
            {
                if (!AbsolutePath.TryCreate(pathTable, argument, out AbsolutePath path))
                {
                    Console.Error.WriteLine($"Could not parse the directory to block '{argument}' as a path.");
                    return 1;
                }

                directoriesToBlock.Add(path);
            }
            
            var sandboxDemo = new BlockingEnumerator(pathTable);
            var result = sandboxDemo.EnumerateWithBlockedDirectories(directoryToEnumerate, directoriesToBlock).GetAwaiter().GetResult();

            var allAccesses = result
                .FileAccesses
                .Select(access => $"{(access.Status == FileAccessStatus.Denied ? "Denied" : "Allowed")} -> {RequestedAccessToString(access.RequestedAccess)} {access.GetPath(pathTable)}")
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            Console.WriteLine($"Enumerated the directory '{args[0]}'. The following accesses were reported:");
            Console.WriteLine(string.Join(Environment.NewLine, allAccesses));

            return 0;
        }

        private static string RequestedAccessToString(RequestedAccess requestedAccess)
        {
            switch (requestedAccess)
            {
                case RequestedAccess.Enumerate:
                case RequestedAccess.EnumerationProbe:
                    return "[Enumerate]";
                case RequestedAccess.Probe:
                    return "[Probe]";
                case RequestedAccess.Read:
                    return "[Read]";
                case RequestedAccess.Write:
                    return "[Write]";
            }

            return string.Empty;
        }

        private static void PrintUsage()
        {
            var processName = Process.GetCurrentProcess().ProcessName;
            Console.WriteLine($"{processName} <directoryToEnumerate> [<directoryToBlock1>] ... [<directoryToBlockN>]");
        }
    }
}
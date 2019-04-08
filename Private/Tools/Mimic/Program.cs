// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Tool.Mimic
{
    /// <summary>
    /// Mimics a process
    /// </summary>
    public static class Program
    {
        public static void Main(string[] arguments)
        {
            Args args = new Args(arguments);
            try
            {
                // File accesses can be defined in 2 forms: Either the actual observed accesses are serialized to a file
                // or they can be specified on the command line. The external file takes presidence.
                if (args.ObservedAccesses != null)
                {
                    args.ReadFiles.Clear();
                    args.ProbeAbsentFiles.Clear();
                    args.EnumerateDirectories.Clear();

                    using (StreamReader reader = new StreamReader(args.ObservedAccesses))
                    {
                        const string ObservedAccessesVersion = "1";
                        string version = reader.ReadLine();
                        if (version != ObservedAccessesVersion)
                        {
                            Console.Error.WriteLine("ObservedAccesses version doesn't match expected version: " + ObservedAccessesVersion);
                            Environment.Exit(-1);
                        }

                        if (args.ObservedAccessesRoot == null)
                        {
                            Console.Error.WriteLine("/observedAccessesRoot is required when /observedAccesses is specified");
                            Environment.Exit(-1);
                        }

                        while (!reader.EndOfStream)
                        {
                            string path = reader.ReadLine();
                            path = Path.Combine(args.ObservedAccessesRoot, path);
                            switch (reader.ReadLine())
                            {
                                case "A":
                                    args.ProbeAbsentFiles.Add(new ProbeAbsentFile(path));
                                    break;
                                case "D":
                                    args.EnumerateDirectories.Add(new EnumerateDirectory(path));
                                    break;
                                case "F":
                                    args.ReadFiles.Add(new ReadFile(path));
                                    break;
                                default:
                                    Console.Error.WriteLine("ObservedAccesses file is not the correct format. " + args.ObservedAccesses);
                                    Environment.Exit(-1);
                                    break;
                            }
                        }
                    }
                }

                foreach (ReadFile read in args.ReadFiles)
                {
                    read.Read(args.AllowMissingInputs, args.IgnoreFilesOverlappingDirectories);
                }

                foreach (WriteFile write in args.WriteFiles)
                {
                    write.Write(args.CreateOutputDirectories, args.IgnoreFilesOverlappingDirectories, args.IOScaleFactor);
                }

                foreach (EnumerateDirectory enumerate in args.EnumerateDirectories)
                {
                    enumerate.Enumerate();
                }

                foreach (ProbeAbsentFile probe in args.ProbeAbsentFiles)
                {
                    probe.Probe();
                }

                // Spin for remainder of duration
                DateTime processStartTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
                TimeSpan duration = TimeSpan.FromMilliseconds(args.ProcessDuration.TotalMilliseconds * args.RuntimeScaleFactor);
                while (DateTime.UtcNow - processStartTime < duration)
                {
                    if (args.Spin)
                    {
                        Thread.SpinWait(10);
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.Error.Write(ex);
                Environment.Exit(1);
            }
        }
    }
}

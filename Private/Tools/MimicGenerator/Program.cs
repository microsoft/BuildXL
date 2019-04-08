// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Generates spec and input files to mimic a build
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Program main
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA2204")]
        public static void Main(string[] arguments)
        {
            string debug = Environment.GetEnvironmentVariable("MimicGeneratorDebugOnStart");
            if (debug != null && debug != "0")
            {
                Debugger.Launch();
            }

            Stopwatch sw = Stopwatch.StartNew();
            Args args = new Args(arguments);
            if (args.HelpDisplayed)
            {
                return;
            }

            try
            {
                GraphReader reader = new GraphReader(args.JsonGraph, args.ObservedInputs);
                BuildGraph graph = reader.ReadGraph();

                if (args.DuplicateGraph > 1)
                {
                    GraphMultiplier.DuplicateAsParallelGraphs(graph, args.DuplicateGraph, args.MaxPipsPerSpec);
                }

                BuildWriter writer = new BuildWriter(args.Dest, args.WriteInputs, args.InputScaleFactor, graph, args.IgnoreResponseFiles, args.Language);
                if (!writer.WriteBuildFiles())
                {
                    ExitError(sw);
                }

                Console.WriteLine("MimicGenerator completed successfully in {0} seconds.", sw.Elapsed.TotalSeconds);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                ExitError(sw, ex);
            }
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly",
            MessageId = "MimicGenerator")]
        private static void ExitError(Stopwatch sw, Exception ex = null)
        {
            Console.WriteLine("MimicGenerator failed {0} seconds.", sw.Elapsed.TotalSeconds);
            if (ex != null)
            {
                Console.Error.Write(ex);
            }

            Environment.Exit(1);
        }
    }
}

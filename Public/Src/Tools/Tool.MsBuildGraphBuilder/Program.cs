// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.MsBuild.Serialization;
using Newtonsoft.Json;

namespace MsBuildGraphBuilderTool
{
    /// <nodoc/>
    public class Program
    {
        /// <summary>
        /// Main entry point for Tool.MsBuildGraphBuilder
        /// </summary>
        /// <remarks>
        /// This tool is supposed to always succeed with exit code 0. Any problems found during graph construction are also serialized in the result.
        /// Any situation where the tool exits with non-zero exit code represents an unexpected error.
        /// In case of an unexpected exception, the tool exits with exit code 1 and writes to standard error the associated exception.
        /// In case of any warnings that don't affect the tool outcome, standard error is also used to report them, but the exit code is 0.
        /// The tool also creates a dedicated pipe where message reporting is sent during graph construction, mostly for user-facing updates purposes.
        /// </remarks>
        /// <param name="args">
        /// args[0]: path to the file where the arguments are serialized
        /// </param>
        static int Main(string[] args)
        {
            try
            {
                // Handy env var for debugging
                if (Environment.GetEnvironmentVariable("MsBuildGraphBuilderDebugOnStart") == "1")
                {
                    Debugger.Launch();
                }

                if (args.Length < 1)
                {
                    Console.Error.WriteLine(Usage());
                    Environment.Exit(1);
                }

                var arguments = DeserializeArguments(args[0]);

                MsBuildGraphBuilder.BuildGraphAndSerialize(arguments);

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected exception: {ex}");
                return 1;
            }
        }

        private static MSBuildGraphBuilderArguments DeserializeArguments(string argumentFile)
        {
            var serializer = JsonSerializer.Create(ProjectGraphSerializationSettings.Settings);
 
            MSBuildGraphBuilderArguments arguments;
            using (StreamReader sr = new StreamReader(argumentFile))
            using (JsonReader reader = new JsonTextReader(sr))
            {
                arguments = serializer.Deserialize<MSBuildGraphBuilderArguments>(reader);
            }

            return arguments;
        }

        private static string Usage()
        {
            return $"{Process.GetCurrentProcess().MainModule.ModuleName} <path to argument file>]";
        }
    }
}

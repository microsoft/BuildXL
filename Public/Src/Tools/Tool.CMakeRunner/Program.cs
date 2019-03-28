// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using BuildXL.FrontEnd.CMake.Serialization;
using Newtonsoft.Json;
using BuildXL.ToolSupport;

namespace NinjaGraphBuilderTool
{
    /// <summary>
    /// Main entry point for Tool.CMakeRunner.
    /// This tool invokes our modified cmake.exe and generates a Ninja build in a subdirectory
    /// </summary>
    /// <param name="args">
    /// args[0]: path to the file where the arguments are serialized
    /// </param>
    public class Program : ToolProgram<CMakeRunnerArguments>    
    {
        private static string Usage => $"Usage: {Process.GetCurrentProcess().MainModule.ModuleName} <path to argument file>";

        private Program()
            : base("CMakeRunner")
        { }

        public static int Main(string[] arguments)
        {
            try
            {
                return new Program().MainHandler(arguments);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"Unexpected exception: {e}");
            }

            return -1;
        }



        public override int Run(CMakeRunnerArguments args)
        {

            if (!TryGetExecutablePath(args.CMakeSearchLocations, out string pathToGraphGenerator))
            {
                Console.Error.WriteLine("Could not find cmake.exe in any of the specified search locations");
                return 1;
            }

            Directory.CreateDirectory(args.BuildDirectory);

            var shouldSaveStandardOutput = !string.IsNullOrEmpty(args.StandardOutputFile);
            var process = new Process
                          {
                              StartInfo =
                              {
                                  WorkingDirectory = args.BuildDirectory,
                                  FileName = pathToGraphGenerator,
                                  Arguments = GetArguments(args),
                                  RedirectStandardOutput = shouldSaveStandardOutput,
                                  RedirectStandardError = false,   
                                  UseShellExecute = false
                              },
                          };

            if (shouldSaveStandardOutput)
            {
                using (var writer = new StreamWriter(args.StandardOutputFile))
                {
                    process.OutputDataReceived += (sender, arguments) => writer.WriteLine(arguments.Data);
                    process.Start();
                    process.BeginOutputReadLine();
                    process.WaitForExit();
                }
            }
            else
            {
                process.Start();
                process.WaitForExit();
            }

            return process.ExitCode;
        }

        private string GetArguments(CMakeRunnerArguments args)
        {
            StringBuilder builder = new StringBuilder("-GNinja");
            if (args.CacheEntries != null)
            {
                foreach (KeyValuePair<string, string> entry in args.CacheEntries)
                {
                    builder.Append(entry.Value != null ? $" -D{entry.Key}={entry.Value}" : " $-U{entry.Key}");
                }
            }

            builder.Append($" {args.ProjectRoot}");
            return builder.ToString();
        }

        private bool TryGetExecutablePath(IEnumerable<string> searchLocations, out string exePath)
        {
            foreach (var directory in searchLocations)
            {
                exePath = Path.Combine(directory, "cmake.exe");
                if (File.Exists(exePath))
                {
                    return true;
                }
            }

            // We couldn't find the executable
            exePath = null;
            return false;
        }

        public override bool TryParse(string[] rawArgs, out CMakeRunnerArguments arguments)
        {
            if (rawArgs.Length < 1)
            {
                Console.Error.WriteLine(Usage);
                arguments = default;
                return false;
            }

            arguments = DeserializeArguments(rawArgs[0]);
            return true;
        }

        private static CMakeRunnerArguments DeserializeArguments(string file)
        {
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
                                                   {
                                                       PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                                                       NullValueHandling = NullValueHandling.Ignore,
                                                       Formatting = Formatting.Indented
                                                   });

            CMakeRunnerArguments arguments;
            using (var sr = new StreamReader(file))
            using (var reader = new JsonTextReader(sr))
            {
                arguments = serializer.Deserialize<CMakeRunnerArguments>(reader);
            }

            return arguments;
        }
    }
}

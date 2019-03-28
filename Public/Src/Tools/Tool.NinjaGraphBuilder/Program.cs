// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.ToolSupport;
using static BuildXL.Utilities.AssemblyHelper;
using static System.Reflection.Assembly;


namespace NinjaGraphBuilderTool
{
    /// <summary>
    /// Main entry point for Tool.NinjaGraphBuilder.
    /// This tool invokes our modified ninja.exe which generates a JSON object
    /// corresponding to the dependency graph that ninja would see
    /// </summary>
    /// <remarks>
    /// This tool is supposed to always succeed with exit code 0. Any problems found during graph construction are also serialized in the result.
    /// Any situation where the tool exits with non-zero exit code represents an unexpected error.
    /// In case of an unexpected exception, the tool exits with exit code 1 and writes to standard error the associated exception.
    /// </remarks>
    /// <param name="args">
    /// args[0]: path to the file where the arguments are serialized
    /// </param>
    public class Program : ToolProgram<NinjaGraphToolArguments>    
    {
        private const string BUILD_NINJA_DEFAULT = "build.ninja";
        private static string Usage => $"Usage: {Process.GetCurrentProcess().MainModule.ModuleName} <path to argument file>";

        // This assumes that Ninjson is deployed as ninjson.exe alongide this NinjaGraphBuilder.exe
        // This should be kept in sync with this tool's deployment scheme in Public/Src/Deployment/buildXL.dsc
        // (and in the Ninja test module)
        private string NinjsonPath => Path.Combine(Path.GetDirectoryName(GetAssemblyLocation(GetExecutingAssembly())), "ninjson.exe");

        private Program()
            : base("NinjaGraphBuilder")
        { }

        public static int Main(string[] arguments)
        {
            try
            {
                return new Program().MainHandler(arguments);
            }
            catch (InvalidArgumentException e)
            {
                Console.Error.WriteLine("Execution error: " + (e.InnerException ?? e).Message);
            }

            return -1;
        }



        public override int Run(NinjaGraphToolArguments args)
        {
            if (!TryGetGraphGeneratorExecutablePath(out string pathToGraphGenerator))
            {
                return 1;
            }
            var targetsString = string.Join(" ", args.Targets);
            var buildFileName = args.BuildFileName ?? BUILD_NINJA_DEFAULT;
            var process = new Process
                          {
                              StartInfo =
                              {
                                  FileName = pathToGraphGenerator,
                                  Arguments = $"-C {args.ProjectRoot} -f {buildFileName} -t json {targetsString}",
                                  RedirectStandardOutput = true,
                                  RedirectStandardError = true,   // Ninja outputs some stuff to standard error sometimes, let's suppress that
                                  UseShellExecute = false
                              },
                          };

            using (var writer = new StreamWriter(args.OutputFile))
            {
                process.OutputDataReceived += (sender, arguments) => writer.WriteLine(arguments.Data);
                process.Start();
                process.BeginOutputReadLine();
                process.WaitForExit();
            }
            
            if (process.ExitCode != 0)
            {
               GenerateErrorResult(process, args);
            }

            return 0;
        }

        private bool TryGetGraphGeneratorExecutablePath(out string path)
        {
            // Prioritize the definition via environment variable
            if (!string.IsNullOrEmpty(path = Environment.GetEnvironmentVariable("CUSTOM_NINJA_PATH")))
            {
                if (File.Exists(path))
                {
                    return true;
                }
                else
                {
                    Console.Error.WriteLine("Warning: the CUSTOM_NINJA_PATH variable is set but doesn't point to an existing file. Using the default path...");
                }
            }

            return File.Exists(path = NinjsonPath); // If this is false something is wrong in BXL's deployment and we can't continue
        }

        private static void GenerateErrorResult(Process process, NinjaGraphToolArguments args)
        {
            process.CancelOutputRead();
            string stdErr = process.StandardError.ReadToEnd();
            stdErr = stdErr.TrimEnd(Environment.NewLine.ToCharArray());   // Remove trailing newlines, makes for tidier error reporting

            var serializer = JsonSerializer.Create(new JsonSerializerSettings
                                                   {
                                                       PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                                                       NullValueHandling = NullValueHandling.Include
                                                   });

            var result = NinjaGraphResult.CreateFailure(stdErr);
            if(File.Exists(args.OutputFile))
            {
                File.Delete(args.OutputFile);
            }

            using (var sw = new StreamWriter(args.OutputFile))
            using (var writer = new JsonTextWriter(sw))
            {
                serializer.Serialize(writer, result);
            }
        }

        public override bool TryParse(string[] rawArgs, out NinjaGraphToolArguments arguments)
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

        private static NinjaGraphToolArguments DeserializeArguments(string file)
        {
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
                                                   {
                                                       PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                                                       NullValueHandling = NullValueHandling.Include,
                                                       Formatting = Formatting.Indented
                                                   });

            NinjaGraphToolArguments arguments;
            using (var sr = new StreamReader(file))
            using (var reader = new JsonTextReader(sr))
            {
                arguments = serializer.Deserialize<NinjaGraphToolArguments>(reader);
            }

            return arguments;
        }
    }
}

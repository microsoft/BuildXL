// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace ResGen.Lite
{
    /// <summary>
    /// Class wrapper for entry point
    /// </summary>
    public class Program
    {
        private static readonly HashSet<string> s_helpArguments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-?",
            "/?",
            "-h",
            "/h",
            "-help",
            "/help"
        };

        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <remarks>
        /// Matches the argument structure of ResGen.exe but works on desktop clr and core clr and generates code compatible with both.
        /// </remarks>
        public static int Main(string[] args)
        {
            if (args.Length < 1 || args.Any(arg => s_helpArguments.Contains(arg)))
            {
                PrintHelp();
                return 0;
            }

            var arguments = new Arguments();
            if (!arguments.TryParse(args, message => Console.Error.WriteLine(message)))
            {
                Console.WriteLine();
                PrintHelp();
                return 1;
            }

            try
            {
                // Load the data
                var data = ResWParser.Parse(arguments.InputFilePath);

                // Generate .resources 
                if (!string.IsNullOrEmpty(arguments.OutputFilePath))
                {
                    ResourcesWriter.Write(arguments.OutputFilePath, data);
                }

                // Generate strongly typed
                if (!string.IsNullOrEmpty(arguments.CodeGenLanguage))
                {
                    SourceCodeWriter.Write(arguments.CodeGenFilePath, data, arguments.CodeGenNamespaceName, arguments.CodeGenClassName, arguments.CodeGenPublicClass, arguments.CodeGenLanguage);
                }

                return 0;
            }
            catch (ResGenLiteException e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
        }

        /// <nodoc />
        private static void PrintHelp()
        {
            Console.WriteLine($@"Copyright (C) Microsoft Corporation.  All rights reserved.

Usage:
   ResGen inputFile.ext [outputFile.resources] [/str:lang[,namespace[,class[,file]]]]
Where .ext is .resX or .resW
This tool only generates .resources files

Options:
-str:<language>[,<namespace>[,<class name>[,<file name>]]]]
                Creates a strongly-typed resource class in the specified
                programming language using Roslyn. In order for the strongly
                typed resource class to work properly, the name of your output
                file without the .resources must match the
                [namespace.]classname of your strongly typed resource class.
                You may need to rename your output file before using it or
                embedding it into an assembly.
-publicClass    Create the strongly typed resource class as a public class.
                This option is ignored if the -str: option is not used.

Language names valid for the -str:<language> option are:
{string.Join(", ", Arguments.SupportedLanguages.Keys)}");
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;

namespace VBCSCompilerLogger
{
    /// <summary>
    /// Parses csc/vbc compiler command line arguments
    /// </summary>
    public static class CompilerUtilities
    {
        /// <summary>
        /// Uses the Roslyn command line parser to understand the arguments passed to the compiler
        /// </summary>
        public static CommandLineArguments GetParsedCommandLineArguments(string language, string arguments, string projectFile)
        {
            Contract.RequiresNotNullOrEmpty(language);
            Contract.RequiresNotNullOrEmpty(arguments);
            Contract.RequiresNotNullOrEmpty(projectFile);

            var sdkDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            var args = CommandLineParser.SplitCommandLineIntoArguments(arguments, removeHashComments: false).ToArray();

            var projectDirectory = Path.GetDirectoryName(projectFile);

            CommandLineArguments result;
            switch (language)
            {
                case LanguageNames.CSharp:
                    result = CSharpCommandLineParser.Default.Parse(args, projectDirectory, sdkDirectory);
                    break;
                case LanguageNames.VisualBasic:
                    result = VisualBasicCommandLineParser.Default.Parse(args, projectDirectory, sdkDirectory);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected language '{language}'");
            }

            return result;
        }
    }
}
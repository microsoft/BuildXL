// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
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
        // Define our delegates.
        private delegate bool TryParseOptionFxn(string arg, out string name, out string value);
        private delegate void ParseResourceDescriptionFxn(
            string resourceDescriptor,
            string baseDirectory,
            bool skipLeadingSeparators, //VB does this
            out string filePath,
            out string fullPath,
            out string fileName,
            out string resourceName,
            out string accessibility);

        // Delegate properties for reflection methods.
        private static TryParseOptionFxn TryParseOption { get; }
        private static ParseResourceDescriptionFxn ParseResourceDescription { get; }

        static CompilerUtilities()
        {
            var tryParseOptionMethod = typeof(CommandLineParser).GetMethod("TryParseOption", BindingFlags.NonPublic | BindingFlags.Static);
            TryParseOption = (TryParseOptionFxn)Delegate.CreateDelegate(typeof(TryParseOptionFxn), null, tryParseOptionMethod);

            var parseResourceDescriptionMethod = typeof(CommandLineParser).GetMethod("ParseResourceDescription", BindingFlags.NonPublic | BindingFlags.Static);
            ParseResourceDescription = (ParseResourceDescriptionFxn)Delegate.CreateDelegate(typeof(ParseResourceDescriptionFxn), null, parseResourceDescriptionMethod);
        }

        /// <summary>
        /// Uses the Roslyn command line parser to understand the arguments passed to the compiler
        /// </summary>
        public static CommandLineArguments GetParsedCommandLineArguments(string language, string arguments, string projectFile, out string[] args)
        {
            Contract.RequiresNotNullOrEmpty(language);
            Contract.RequiresNotNullOrEmpty(arguments);
            Contract.RequiresNotNullOrEmpty(projectFile);

            var sdkDirectory = RuntimeEnvironment.GetRuntimeDirectory();
            args = CommandLineParser.SplitCommandLineIntoArguments(arguments, removeHashComments: false).ToArray();

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

        /// <summary>
        /// Uses the Roslyn command line parser to resolve embedded resources to their file path.
        /// /resource: parameters end up in CommandLineArguments.ManifestResources, but the returned class drops the file path.
        /// Thus we need this method in order to resolve the file paths.
        /// We should be able to remove this if/when this gets resolved: https://github.com/dotnet/roslyn/issues/41372.
        /// </summary>
        /// <param name="embeddedResourceArgs">The embedded resource arguments passed to the compiler.</param>
        /// <param name="baseDirectory">The base directory of the project.</param>
        /// <returns>An array of file paths to the embedded resource inputs.</returns>
        public static string[] GetEmbeddedResourceFilePaths(IEnumerable<string> embeddedResourceArgs, string baseDirectory)
        {
            var embeddedResourceFilePaths = new List<string>();
            foreach (string embeddedResourceArg in embeddedResourceArgs)
            {
                bool parsed = TryParseOption(embeddedResourceArg, out string argName, out string argValue);
                if (parsed)
                {
                    ParseResourceDescription(
                        argValue,
                        baseDirectory,
                        skipLeadingSeparators: false,
                        out string filePath,
                        out string fullPath,
                        out string fileName,
                        out string resourceName,
                        out string accessibility);

                    embeddedResourceFilePaths.Add(fullPath);
                }
            }

            return embeddedResourceFilePaths.ToArray();
        }
    }
}
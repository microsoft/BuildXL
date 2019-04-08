// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.ToolSupport;

namespace BuildXL.LogGen
{
    internal sealed class Configuration : CommandLineUtilities
    {
        private readonly bool m_helpRequested;
        internal readonly string OutputCSharpFile;
        internal readonly string Namespace;
        internal readonly string TargetRuntime;
        internal readonly string TargetFramework;
        internal readonly List<string> References = new List<string>();
        internal readonly List<string> SourceFiles = new List<string>();
        internal readonly List<string> PreprocessorDefines = new List<string>();
        internal readonly Dictionary<string, string> Aliases = new Dictionary<string, string>();

        internal Configuration(string[] args)
            : base(args)
        {
            // Parse the command line options
            foreach (Option opt in Options)
            {
                Func<string, bool> match = n => string.Equals(opt.Name, n, StringComparison.OrdinalIgnoreCase);

                if (match("reference") || match("r"))
                {
                    References.AddRange(ParseRepeatingPathOption(opt, ";"));
                }
                else if (match("help") || match("?"))
                {
                    ParseVoidOption(opt);
                    m_helpRequested = true;
                }
                else if (match("s"))
                {
                    SourceFiles.AddRange(ParseRepeatingPathOption(opt, ";"));
                }
                else if (match("d"))
                {
                    PreprocessorDefines.AddRange(opt.Value.Split(';'));
                }
                else if (match("a"))
                {
                    var kv = ParseKeyValuePair(opt);
                    Aliases[kv.Key] = kv.Value;
                }
                else if (match("output"))
                {
                    OutputCSharpFile = ParseSingletonPathOption(opt, OutputCSharpFile);
                }
                else if (match("namespace"))
                {
                    Namespace = ParseSingletonStringOption(opt, Namespace);
                }
                else if (match("targetFramework"))
                {
                    TargetFramework = ParseSingletonStringOption(opt, TargetFramework);
                }
                else if (match("targetRuntime"))
                {
                    TargetRuntime = ParseSingletonStringOption(opt, TargetRuntime);
                }
                else
                {
                    throw Error("Command line argument '{0}' is not recognized.", opt.Name);
                }
            }

            if (args.Length == 0)
            {
                m_helpRequested = true;
            }

            if (m_helpRequested)
            {
                DisplayHelpAndExit();
            }
            else
            {
                if (OutputCSharpFile == null)
                {
                    throw Error("OutputCSharpFile is required");
                }
                else if (Namespace == null)
                {
                    throw Error("Namespace is required");
                }
            }

            foreach (var alias in Aliases)
            {
                if (alias.Key.Contains("{") || alias.Key.Contains("}"))
                {
                    throw Error($"Alias key '{alias.Key}' is not allowed to have a curly bracket '{{' or '}}'.");
                }

                if (alias.Value.Contains("{") || alias.Value.Contains("}"))
                {
                    throw Error($"Alias value '{alias.Value}' is not allowed to have a curly bracket '{{' or '}}'.");
                }
            }

            if (string.IsNullOrEmpty(TargetFramework))
            {
                throw Error("TargetFramework is required");
            }

            if (string.IsNullOrEmpty(TargetRuntime))
            {
                throw Error("TargetRuntime is required");
            }
        }

        private static void DisplayHelpAndExit()
        {
            var hw = new HelpWriter();
            hw.WriteLine("Microsoft (R) Build Accelerator LogGen");
            hw.WriteLine("Copyright (C) Microsoft Corporation. All rights reserved.");

            hw.WriteLine("Generates logging for BuildXL.");
            hw.WriteBanner("Usage");
            hw.WriteLine();
            hw.WriteOption(
                "output:<path>",
                "Full path to desired output cs file.",
                shortName: "o");
            hw.WriteOption(
                "reference:<path>",
                "References of the assembly passed. This is needed in case the .Net loader can't find the references using the standard .Net loading techniques.",
                shortName: "r");
            hw.WriteOption(
                "a:<key>=<value>",
                "Allows predefined strings to be used in log messages.");
            hw.WriteOption(
                "s:<path>",
                "Full path to source file.");
            hw.WriteOption(
                "namespace:<string>",
                "Namespace generated logging should be under.");
            hw.WriteOption(
                "targetFramework:<string>",
                "The dotnet target runtime to generate logging for.");
            hw.WriteOption(
                "targetRuntime:<string>",
                "The dotnet target runtime to generate logging for [ win-x64 | osx-x64 ]");

            Environment.Exit(0);
        }
    }
}

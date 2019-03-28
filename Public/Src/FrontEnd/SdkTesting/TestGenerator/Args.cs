// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.ToolSupport;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.Testing.TestGenerator
{
    /// <summary>
    /// Arguments
    /// </summary>
    public sealed class Args : CommandLineUtilities
    {
        /// <summary>
        /// Whether help text was displayed. When true the application should exit without using the Args
        /// </summary>
        public bool HelpDisplayed = false;

        /// <summary>
        /// Files to test
        /// </summary>
        public List<string> TestFiles { get; } = new List<string>();

        /// <summary>
        /// The Lkg files for the tests
        /// </summary>
        public Dictionary<string, string> LkgFiles { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// SdkFolders under test
        /// </summary>
        public List<string> SdksToTest { get; } = new List<string>();

        /// <summary>
        /// The folder where to generate the C# files in.
        /// </summary>
        public string OutputFolder { get; private set; }

        private static readonly string[] s_helpStrings = new[] { "?", "help" };

        /// <nodoc />
        public Args(IReadOnlyCollection<string> args)
            : base(args)
        {
            foreach (Option opt in Options)
            {
                if (opt.Name.Equals("OutputFolder", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    OutputFolder = ParsePathOption(opt);
                }
                else if (opt.Name.Equals("TestFile", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("t", StringComparison.OrdinalIgnoreCase))
                {
                    var testFile = ParsePathOption(opt);
                    TestFiles.Add(testFile);
                }
                else if (opt.Name.Equals("SdkToTest", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("s", StringComparison.OrdinalIgnoreCase))
                {
                    var sdkFolder = ParsePathOption(opt);
                    SdksToTest.Add(sdkFolder);
                }
                else if (opt.Name.Equals("LkgFile", StringComparison.OrdinalIgnoreCase) || opt.Name.Equals("l", StringComparison.OrdinalIgnoreCase))
                {
                    var lkgFile = ParsePathOption(opt);
                    var lkgKey = ComputeLkgKey(lkgFile);
                    string existingFile;
                    if (LkgFiles.TryGetValue(lkgKey, out existingFile))
                    {
                        throw Error(C($"Duplicate LkgFile defined: '{lkgFile}' and '{existingFile}' "));
                    }

                    LkgFiles.Add(lkgKey, lkgFile);
                }
                else if (s_helpStrings.Any(s => opt.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
                {
                    WriteHelp();
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(OutputFolder))
            {
                throw Error("OutputFolder parameter is required");
            }

            if (TestFiles.Count == 0)
            {
                throw Error("At least one TestFile is required");
            }
        }

        /// <summary>
        /// Computes the predicted lkg key based on the lkgFiles path.
        /// </summary>
        public static string ComputeLkgKey(string lkgFile)
        {
            return Path.GetFileName(Path.GetDirectoryName(lkgFile)) + "#" + Path.GetFileNameWithoutExtension(lkgFile);
        }

        /// <summary>
        /// Computes the predicted lkg key based on the test file and the identifier path.
        /// </summary>
        public static string ComputeLkgKey(string testFile, string fullName)
        {
            return Path.GetFileNameWithoutExtension(testFile) + "#" + fullName;
        }

        private void WriteHelp()
        {
            HelpWriter writer = new HelpWriter();
            writer.WriteBanner("TestGenerator.exe - Creates C# test files for the given DScript unit tests.");

            writer.WriteOption("OutputFolder", "Required. Path to the json graph.", shortName: "o");
            writer.WriteOption("TestFile", "At least one required. DScript file with tests", shortName: "t");
            writer.WriteOption("SdkToTest", "Sdk folders that the test files should have access to.", shortName: "s");

            HelpDisplayed = true;
        }
    }
}

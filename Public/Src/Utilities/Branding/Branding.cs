// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Reflection;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Helper file to extract version information from BuildXL
    /// </summary>
    /// <remarks>
    /// This relies on the build placing a file BuildXL.manifest next to the assembly.
    /// On first use of a field in this class we will read this file once and store some of the manifest data
    /// </remarks>
    public static class Branding
    {
        private static readonly string s_shortProductName = "BuildXL(DevBuild)";
        private static readonly string s_longProductName = "Microsoft (R) Build Accelerator (DevBuild)";
        private static readonly string s_version = "0.0.0";
        private static readonly string s_sourceVersion = "DevBuild";
        private static readonly string s_productExecutableName = "bxl.exe";
        private static readonly string s_analyzerExecutableName = "bxlAnalyzer.exe";
        
        /// <summary>
        /// Initializes the static fields
        /// </summary>
        /// <remarks>
        /// This function handles the manifest not being there for test and developer builds, but is very strict in the format
        /// since the generation and reading are inherently linked to the exact same build.
        /// </remarks>
        static Branding()
        {
            var assemblyRoot = Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(Assembly.GetExecutingAssembly()));

            var manifestFilePath = Path.Combine(assemblyRoot, "BuildXL.manifest");
            if (File.Exists(manifestFilePath))
            {
                var lines = File.ReadAllLines(manifestFilePath);
                Contract.Assert(lines.Length == 6, "Branding file is malformed. Encountered wrong number of lines.");

                int line = 0;
                s_shortProductName = lines[line++];
                s_longProductName = lines[line++];
                s_version = lines[line++];
                s_sourceVersion = lines[line++];
                s_productExecutableName = lines[line++];
                s_analyzerExecutableName = lines[line++];

                Contract.Assert(!string.IsNullOrEmpty(s_shortProductName), "Branding file is malformed. Short product name is malformed.");
                Contract.Assert(!string.IsNullOrEmpty(s_longProductName), "Branding file is malformed. Long product name is malformed.");
                Contract.Assert(!string.IsNullOrEmpty(s_version), "Branding file is malformed. Version is malformed.");
                Contract.Assert(!string.IsNullOrEmpty(s_sourceVersion), "Branding file is malformed. SourceVersion is malformed.");
                Contract.Assert(!string.IsNullOrEmpty(s_productExecutableName), "Branding file is malformed. ProductExecutableName is malformed.");
                Contract.Assert(!string.IsNullOrEmpty(s_analyzerExecutableName), "Branding file is malformed. AnalyzerExecutableName is malformed.");
            }
        }

        /// <summary>
        /// Returns the current BuildXL version set by a build agent.
        /// </summary>
        public static string ShortProductName => s_shortProductName;

        /// <summary>
        /// Returns the current BuildXL version set by a build agent.
        /// </summary>
        public static string LongProductName => s_longProductName;

        /// <summary>
        /// Returns the current BuildXL version set by a build agent.
        /// </summary>
        public static string Version => s_version;

        /// <summary>
        /// Returns commit Id of the current BuildXL version.
        /// </summary>
        public static string SourceVersion => s_sourceVersion;

        /// <summary>
        /// Returns the current BuildXL main executable name
        /// </summary>
        public static string ProductExecutableName => s_productExecutableName;

        /// <summary>
        /// Returns the current BuildXL analyzer executable name
        /// </summary>
        public static string AnalyzerExecutableName => s_analyzerExecutableName;
    }
}

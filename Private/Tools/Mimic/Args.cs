// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.ToolSupport;

namespace Tool.Mimic
{
    internal sealed class Args : CommandLineUtilities
    {
        public List<ReadFile> ReadFiles = new List<ReadFile>();
        public string ObservedAccesses = null;
        public string ObservedAccessesRoot = null;
        public List<WriteFile> WriteFiles = new List<WriteFile>();
        public List<EnumerateDirectory> EnumerateDirectories = new List<EnumerateDirectory>();
        public List<ProbeAbsentFile> ProbeAbsentFiles = new List<ProbeAbsentFile>();
        public TimeSpan ProcessDuration = new TimeSpan(0);
        public bool AllowMissingInputs = false;
        public bool CreateOutputDirectories = false;
        public bool IgnoreFilesOverlappingDirectories = false;
        public double IOScaleFactor = 1;
        public double RuntimeScaleFactor = 1;
        public bool Spin = true;

        private static readonly string[] s_helpStrings = new[] { "?", "help" };

        public Args(string[] args)
            : base(args)
        {
            foreach (Option opt in Options)
            {
                if (opt.Name.Equals("DurationMS", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessDuration = TimeSpan.FromMilliseconds(ParseInt32Option(opt, 0, int.MaxValue));
                }
                else if (opt.Name.Equals("Input", StringComparison.OrdinalIgnoreCase))
                {
                    ReadFiles.Add(new ReadFile(ParsePathOption(opt)));
                }
                else if (opt.Name.Equals("observedAccesses", StringComparison.OrdinalIgnoreCase))
                {
                    ObservedAccesses = ParsePathOption(opt);
                }
                else if (opt.Name.Equals("observedAccessesRoot", StringComparison.OrdinalIgnoreCase))
                {
                    ObservedAccessesRoot = ParsePathOption(opt);
                }
                else if (opt.Name.Equals("Output", StringComparison.OrdinalIgnoreCase))
                {
                    string[] split = opt.Value.Split('|');

                    if (split.Length != 3)
                    {
                        throw Error("Output option must be of form: /Output:<path>|<lengthInBytes>|<repeatingContent>");
                    }

                    string path = Path.GetFullPath(TrimPathQuotation(split[0]));
                    long length;

                    if (!long.TryParse(split[1], out length))
                    {
                        throw Error("Could not parse '{0}' to a long for /Output option file length.", split[1]);
                    }

                    var mimicSalt = Environment.GetEnvironmentVariable("mimicSalt");

                    WriteFiles.Add(new WriteFile(path, length, split[2] + (!string.IsNullOrWhiteSpace(mimicSalt) ? mimicSalt : string.Empty)));
                }
                else if (opt.Name.StartsWith("AllowMissingInputs", StringComparison.OrdinalIgnoreCase))
                {
                    AllowMissingInputs = ParseBooleanOption(opt);
                }
                else if (opt.Name.StartsWith("CreateOutputDirectories", StringComparison.OrdinalIgnoreCase))
                {
                    CreateOutputDirectories = ParseBooleanOption(opt);
                }
                else if (opt.Name.StartsWith("IgnoreFilesOverlappingDirectories", StringComparison.OrdinalIgnoreCase))
                {
                    IgnoreFilesOverlappingDirectories = ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("IOScaleFactor", StringComparison.OrdinalIgnoreCase))
                {
                    IOScaleFactor = ParseDoubleOption(opt, 0, double.MaxValue);
                }
                else if (opt.Name.Equals("RuntimeScaleFactor", StringComparison.OrdinalIgnoreCase))
                {
                    RuntimeScaleFactor = ParseDoubleOption(opt, 0, double.MaxValue);
                }
                else if (opt.Name.StartsWith("Spin", StringComparison.OrdinalIgnoreCase))
                {
                    Spin = ParseBooleanOption(opt);
                }
                else if (s_helpStrings.Any(s => opt.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
                {
                    WriteHelp();
                }
            }

            if (ProcessDuration.Ticks == 0)
            {
                throw Error("DurationMS parameter is required");
            }
        }

        private static void WriteHelp()
        {
            HelpWriter writer = new HelpWriter();
            writer.WriteBanner("Mimic.exe - Mimics a process performing reads and writes. After reads and writes the process will spin until the requested duration is reached.");

            writer.WriteOption("DurationMs", "Required. Duration in milliseconds the process should run.");
            writer.WriteOption("Input", "A file that will be read sequentially to the end");
            writer.WriteOption("Output", "A file that will be written sequentially for a specified number of bytes");
            writer.WriteOption("AllowMissingInputs", "Performs a file existence check and no-ops reading an input if files are missing. Disabled by default.");
            writer.WriteOption("CreateOutputDirectories", "Creates directories for output files if they don't already exist. Disabled by default.");
            writer.WriteOption("IgnoreFilesOverlappingDirectories", "Skips attempting to create a file at a path if a directory already exists there.");
            writer.WriteOption("RuntimeScaleFactor", "Scales DurationMs.");
            writer.WriteOption("IOScaleFactor", "Scales IO by the factor. If smaller than 1, only part of inputs will be read. If larger, inputs will be read multiple times");
            writer.WriteOption("Spin", "True if the process should spin for its runtime. If false the process will sleep. Defaults to true");
        }

        /// <summary>
        /// Removes surrounding single or double quotes from a path.
        /// We need specific logic for Mimic here so this method hides the implementation in the base class.
        /// No need to put mimic specific logic to the 'BuildXL.ToolSupport'.
        /// </summary>
        public new static string TrimPathQuotation(string path)
        {
            foreach (char c in new char[] { '"', '\'' })
            {
                if (path.Length > 2 && path[0] == c && path[path.Length - 1] == c)
                {
                    path = path.Substring(1, path.Length - 2);
                }
                else if (path.Length > 2 && path[0] == c)
                {
                    // Consider DS specs for Mimic. The quotes surround the whole arg including 'repeatingContent' and 'filelength'.
                    // example: "\"C:\\find build.disco|685|9011d941953457ac27515fdcce3029187d376cae\""
                    path = path.Substring(1, path.Length - 1);
                }
            }

            return path;
        }
    }
}

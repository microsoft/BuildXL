// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.ToolSupport;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// Command line arguments for MimicGenerator
    /// </summary>
    internal sealed class Args : CommandLineUtilities
    {
        /// <summary>
        /// Whether help text was displayed. When true the application should exit without using the Args
        /// </summary>
        public bool HelpDisplayed = false;

        /// <summary>
        /// Full path of JsonGraph input
        /// </summary>
        public string JsonGraph;

        /// <summary>
        /// Full path of the ObservedInputs file
        /// </summary>
        public string ObservedInputs;

        /// <summary>
        ///  Full path to destination to write files
        /// </summary>
        public string Dest;

        /// <summary>
        /// True if input files should be written
        /// </summary>
        public bool WriteInputs = false;

        /// <summary>
        /// True if response file pips from source build should be excluded from created mimic build
        /// </summary>
        public bool IgnoreResponseFiles = false;

        /// <summary>
        /// Scale factor for input files
        /// </summary>
        public double InputScaleFactor = 1;

        /// <summary>
        /// Number of times to duplicate the graph
        /// </summary>
        public int DuplicateGraph = 0;

        /// <summary>
        /// Maximum number of pips to include in a spec file before breaking it into a second spec file
        /// </summary>
        public int MaxPipsPerSpec = int.MaxValue;

        /// <summary>
        /// Which language will be used to generate specs, module, and config files
        /// </summary>
        public Language Language;

        private static readonly string[] s_helpStrings = new[] { "?", "help" };

        /// <inhertdoc/>
        public Args(string[] args)
            : base(args)
        {
            foreach (Option opt in Options)
            {
                if (opt.Name.Equals("JsonGraph", StringComparison.OrdinalIgnoreCase))
                {
                    JsonGraph = ParsePathOption(opt);
                }

                if (opt.Name.Equals("ObservedInputs", StringComparison.OrdinalIgnoreCase))
                {
                    ObservedInputs = ParsePathOption(opt);
                }
                else if (opt.Name.Equals("Dest", StringComparison.OrdinalIgnoreCase))
                {
                    Dest = ParsePathOption(opt);
                }
                else if (opt.Name.Equals("WriteInputs", StringComparison.OrdinalIgnoreCase))
                {
                    WriteInputs = ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("InputScaleFactor", StringComparison.OrdinalIgnoreCase))
                {
                    InputScaleFactor = ParseDoubleOption(opt, 0, double.MaxValue);
                }
                else if (opt.Name.Equals("IgnoreResponseFiles", StringComparison.OrdinalIgnoreCase))
                {
                    IgnoreResponseFiles = ParseBooleanOption(opt);
                }
                else if (opt.Name.Equals("Language", StringComparison.OrdinalIgnoreCase))
                {
                    Language = ParseEnumOption<Language>(opt);
                }
                else if (opt.Name.Equals("DuplicateGraph", StringComparison.OrdinalIgnoreCase))
                {
                    DuplicateGraph = ParseInt32Option(opt, 0, int.MaxValue);
                }
                else if (opt.Name.Equals("MaxPipsPerSpec", StringComparison.OrdinalIgnoreCase))
                {
                    MaxPipsPerSpec = ParseInt32Option(opt, 0, int.MaxValue);
                }
                else if (s_helpStrings.Any(s => opt.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
                {
                    WriteHelp();
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(JsonGraph))
            {
                throw Error("JsonGraph parameter is required");
            }
            else if (string.IsNullOrWhiteSpace(Dest))
            {
                throw Error("Dest parameter is required");
            }
            else if (Language == Language.None)
            {
                throw Error("Language parameter is required");
            }
        }

        private void WriteHelp()
        {
            HelpWriter writer = new HelpWriter();
            writer.WriteBanner("MimicGenerator.exe - Creates spec files to perform mimiced build.");

            writer.WriteOption("JsonGraph", "Required. Path to the json graph.");
            writer.WriteOption("ObservedInputs", "Optional. Dump of observed file accesses. When specified, pips will mimic these accesses.");
            writer.WriteOption("Dest", "Required. Destination for generated specs.");
            writer.WriteOption("WriteInputs", "Whether input files should be written in addition to BuildXL files. Defaults to false.");
            writer.WriteOption("InputScaleFactor", "Scale factor to increase or decrease the size of input files. Defaults to 1.");
            writer.WriteOption("IgnoreResponseFiles", "Whether response files from the input build should be ignored in mimiced build. Defaults to false.");
            writer.WriteOption("Language", "Language of files to write. Available options: 'DScript'");
            writer.WriteOption("DuplicateGraph", "Duplicates the graph the number of times specified");

            HelpDisplayed = true;
        }
    }
}

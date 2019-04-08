// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.ToolSupport;

namespace BuildXL.IDE.CreateZipPackage
{
    internal sealed class Args : CommandLineUtilities
    {
        private readonly List<string> m_files = new List<string>();

        public string InputDirectory { get; private set; }
        public string OutputFile { get; private set; }
        public bool UseUriEncoding { get; private set; }
        public bool FixUnixPermissions { get; private set; } = false;
        public string OutputFilesRoot { get; private set; }

        public IReadOnlyList<string> Files => m_files;

        public Args(string[] args)
            : base(args)
        {
            foreach (var option in Options)
            {
                Console.WriteLine($"Name: {option.Name}, value: {option.Value}");

                if (option.Name.Equals("inputDirectory", StringComparison.InvariantCultureIgnoreCase))
                {
                    InputDirectory = option.Value;
                }
                else if (option.Name.Equals("outputFile", StringComparison.InvariantCultureIgnoreCase))
                {
                    OutputFile = option.Value;
                }
                else if (option.Name.Equals("uriEncoding+", StringComparison.InvariantCultureIgnoreCase))
                {
                    UseUriEncoding = true;
                }
                else if (option.Name.Equals("fixUnixPermissions+", StringComparison.InvariantCultureIgnoreCase))
                {
                    FixUnixPermissions = true;
                }
                else if (option.Name.Equals("inputFilesRoot", StringComparison.InvariantCultureIgnoreCase))
                {
                    OutputFilesRoot = option.Value;
                }
            }

            // Copy all arguments (i.e. options with no leading '/' to file list.
            m_files.AddRange(Arguments);

            Validate();
        }

        private void Validate()
        {
            if (OutputFile == null)
            {
                throw Error("'/outputFile' is a required argument.");
            }

            if (!string.IsNullOrEmpty(InputDirectory) && !Directory.Exists(InputDirectory))
            {
                throw Error($"Given input directory '{InputDirectory}' should point to a valid directory.");
            }

            if (string.IsNullOrEmpty(InputDirectory) && m_files.Count == 0)
            {
                throw Error("Either intput directory (/inputDirectory) or a list of files should be provided.");
            }

            foreach (var file in m_files)
            {
                if (!File.Exists(file))
                {
                    throw Error($"Given file '{file}' should exist on disk.");
                }
            }
        }

        public static void WriteHelp()
        {
            HelpWriter writer = new HelpWriter();

            writer.WriteLine("CreateZipPackage.exe usage:");
            writer.WriteBanner("[/inputDirectory:<input directory>] /outputFile:<output file name> [/uriEncoding(+|-)>] [/fixUnixPermissions(+|-)>] [/inputFilesRoot:<relative root for input files>] [<list of files>]");
        }
    }
}

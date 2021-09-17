// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Configuration;

namespace Tool.Download
{
    /// <nodoc/>
    internal sealed partial class ExtractorArgs : CommandLineUtilities
    {
        /// <summary>
        /// Output directory to extract the file to
        /// </summary>
        public string ExtractDirectory { get; }

        /// <summary>
        /// Full path to the file that needs to be extracted
        /// </summary>
        public string PathToFileToExtract { get; }

        /// <summary>
        /// Archive type to extract
        /// </summary>
        public DownloadArchiveType ArchiveType { get; }

        /// <nodoc />
        public ExtractorArgs(string[] args) : base(args)
        {
            foreach (Option opt in Options)
            {
                if (opt.Name.Equals("file", StringComparison.OrdinalIgnoreCase))
                {
                    PathToFileToExtract = opt.Value;

                }
                else if (opt.Name.Equals("extractTo", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractDirectory = opt.Value;
                }
                else if (opt.Name.Equals("archiveType", StringComparison.OrdinalIgnoreCase))
                {
                    ArchiveType = Enum.Parse<DownloadArchiveType>(opt.Value, ignoreCase: true);
                }
                else
                {
                    throw Error($"Unsupported option: {opt.Name}.");
                }
            }

            if (string.IsNullOrEmpty(ExtractDirectory))
            {
                throw Error($"Missing mandatory argument 'extractTo'");
            }

            if (string.IsNullOrEmpty(PathToFileToExtract))
            {
                throw Error($"Missing mandatory argument 'file'");
            }
        }
    }
}

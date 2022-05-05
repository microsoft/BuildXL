// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.ToolSupport;
using NuGet.Versioning;

namespace Tool.Download
{
    /// <nodoc/>
    internal sealed partial class NugetDownloaderArgs : CommandLineUtilities
    {
        /// <summary>
        /// package id of the download
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// package version of the download
        /// </summary>
        public NuGetVersion Version { get; }

        /// <summary>
        /// The directory to place the downloaded file
        /// </summary>
        public string DownloadDirectory { get; }

        /// <summary>
        /// The Nuget feeds (name and Uri) where to download the package from
        /// </summary>
        public IReadOnlyDictionary<string, Uri> Repositories { get; }

        /// <nodoc />
        public NugetDownloaderArgs(string[] args)
            : base(args)
        {
            var repositories = new Dictionary<string, Uri>();

            foreach (Option opt in Options)
            {
                if (opt.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                {
                    Id = opt.Value;
                }
                else if (opt.Name.Equals("version", StringComparison.OrdinalIgnoreCase))
                {
                    if (!NuGetVersion.TryParse(opt.Value, out NuGetVersion version))
                    {
                        throw Error($"Malformed version: {opt.Value}.");
                    }

                    Version = version;
                }
                else if (opt.Name.Equals("downloadDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    DownloadDirectory = opt.Value;
                }
                else if (opt.Name.Equals("repositories", StringComparison.OrdinalIgnoreCase))
                {
                    var kvp = ParseKeyValuePair(opt);
                    repositories[kvp.Key] = new Uri(kvp.Value);
                }
                else
                {
                    throw Error($"Unsupported option: {opt.Name}.");
                }
            }

            Repositories = repositories;

            if (Id == null)
            {
                throw Error($"Missing mandatory argument 'id'");
            }

            if (Version == null)
            {
                throw Error($"Missing mandatory argument 'version'");
            }

            if (Repositories.Count == 0)
            {
                throw Error($"Missing mandatory argument 'repositories'");
            }

            if (string.IsNullOrWhiteSpace(DownloadDirectory))
            {
                throw Error($"Missing mandatory argument 'DownloadDirectory'");
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.ToolSupport;

namespace Tool.Download
{
    /// <nodoc/>
    internal sealed partial class DownloaderArgs : CommandLineUtilities
    {
        /// <summary>
        /// Url of the download
        /// </summary>
        public Uri Url { get; }

        /// <summary>
        /// Optional filename. By default the filename for the download is determined from the URL, but can be overridden when the url is obscure.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// Optional hash of the downloaded file to ensure safe robust builds and correctness. When specified the download is validated against this hash.
        /// </summary>
        public ContentHash? Hash { get; } 

        /// <summary>
        /// The directory to place the downloaded file
        /// </summary>
        public string DownloadDirectory { get; }

        /// <summary>
        /// Fully qualified path to the downloaded file
        /// </summary>
        public string DownloadPath => Path.Combine(DownloadDirectory, FileName);

        /// <nodoc />
        public DownloaderArgs(string[] args)
            : base(args)
        {
            foreach (Option opt in Options)
            {
                if (opt.Name.Equals("url", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(opt.Value, UriKind.Absolute, out var url))
                    {
                        Url = url;
                    }
                    else
                    {
                        throw Error($"Invalid URI: {opt.Value}");
                    }

                }
                else if (opt.Name.Equals("fileName", StringComparison.OrdinalIgnoreCase))
                {
                    FileName = opt.Value;
                }
                else if (opt.Name.Equals("hash", StringComparison.OrdinalIgnoreCase))
                {

                    if (!ContentHash.TryParse(opt.Value, out var hash))
                    {
                        throw Error($"Invalid hash value '{opt.Value}' for '{Url}'. It must be a valid content hash format i.e. 'VSO0:000000000000000000000000000000000000000000000000000000000000000000'.") ;
                    }

                    Hash = hash;
                }
                else if (opt.Name.Equals("downloadDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    DownloadDirectory = opt.Value;
                }
                else
                {
                    throw Error($"Unsupported option: {opt.Name}.");
                }
            }

            if (Url == null)
            {
                throw Error($"Missing mandatory argument 'Url'");
            }

            if (string.IsNullOrWhiteSpace(DownloadDirectory))
            {
                throw Error($"Missing mandatory argument 'DownloadDirectory'");
            }

            if (string.IsNullOrEmpty(FileName))
            {
                FileName = Path.GetFileName(Url.AbsolutePath.TrimEnd(new[] { '/', '\\' }));
            }
        }
    }
}

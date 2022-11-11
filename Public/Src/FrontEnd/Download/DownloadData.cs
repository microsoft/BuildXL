// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// Extracted data of items to download
    /// </summary>
    public sealed class DownloadData
    {
        /// <summary>
        /// The settings as defined in the resolver
        /// </summary>
        public IDownloadFileSettings Settings { get; }

        /// <nodoc />
        public DownloadData(
            FrontEndContext context,
            IDownloadFileSettings settings,
            Uri downloadUri,
            AbsolutePath resolverRoot,
            ContentHash? contentHash,
            string downloadedValueName = null,
            string extractedValueName = null)
        {
            Contract.Requires(context != null);
            Contract.Requires(settings != null);
            Contract.Requires(downloadUri != null);
            Contract.Requires(resolverRoot.IsValid);

            // Apply defaults if not specified
            downloadedValueName ??= "download";
            extractedValueName ??= "extracted";

            Settings = settings;
            DownloadUri = downloadUri;

            ModuleRoot = resolverRoot.Combine(context.PathTable, settings.ModuleName);

            var fileName = settings.FileName;
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = Path.GetFileName(downloadUri.AbsolutePath.TrimEnd(new[] {'/', '\\'}));
            }
            
            DownloadedFilePath = ModuleRoot.Combine(context.PathTable, "f").Combine(context.PathTable, fileName);

            ContentsFolder = settings.ArchiveType == DownloadArchiveType.File 
                ? DirectoryArtifact.Invalid 
                : DirectoryArtifact.CreateWithZeroPartialSealId(ModuleRoot.Combine(context.PathTable, "c"));

            ModuleConfigFile = ModuleRoot.Combine(context.PathTable, "module.config.dsc");
            ModuleSpecFile = ModuleRoot.Combine(context.PathTable, "project.dsc");

            ContentHash = contentHash;

            DownloadedValueName = downloadedValueName;
            ExtractedValueName = extractedValueName;
        }

        /// <nodoc />
        public bool ShouldExtractBits => Settings.ArchiveType != DownloadArchiveType.File;

        /// <summary>
        /// The parsed download uri of the url field on the DownloadSettings
        /// </summary>
        public Uri DownloadUri { get; }

        /// <nodoc />
        public AbsolutePath ModuleRoot { get; }

        /// <nodoc />
        public AbsolutePath DownloadedFilePath { get; }

        /// <nodoc />
        public DirectoryArtifact ContentsFolder { get; }

        /// <nodoc />
        public AbsolutePath ModuleConfigFile { get; }

        /// <nodoc />
        public AbsolutePath ModuleSpecFile { get; }

        /// <summary>
        /// The optional parsed contenthash of the hash on the DownloadSettings
        /// </summary>
        public ContentHash? ContentHash { get; }

        /// <nodoc />
        public string DownloadedValueName { get; }

        /// <nodoc />
        public string ExtractedValueName { get; }
    }
}

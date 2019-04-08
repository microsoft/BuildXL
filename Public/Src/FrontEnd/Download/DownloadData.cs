// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// Extracted data of items to download
    /// </summary>
    public class DownloadData
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
            ContentHash? contentHash)
        {
            Contract.Requires(context != null);
            Contract.Requires(settings != null);
            Contract.Requires(downloadUri != null);
            Contract.Requires(resolverRoot.IsValid);

            Settings = settings;
            DownloadUri = downloadUri;

            ModuleRoot = resolverRoot.Combine(context.PathTable, settings.ModuleName);

            var fileName = settings.FileName;
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = Path.GetFileName(downloadUri.AbsolutePath.TrimEnd(new[] {'/', '\\'}));
            }
            
            DownloadedFilePath = ModuleRoot.Combine(context.PathTable, "f").Combine(context.PathTable, fileName);

            ContentsFolder = settings.ArchiveType == DownloadArchiveType.File ? DirectoryArtifact.Invalid : DirectoryArtifact.CreateWithZeroPartialSealId(ModuleRoot.Combine(context.PathTable, "c"));

            ModuleConfigFile = ModuleRoot.Combine(context.PathTable, "module.config.dsc");
            ModuleSpecFile = ModuleRoot.Combine(context.PathTable, "project.dsc");
            DownloadManifestFile = ModuleRoot.Combine(context.PathTable, "manifest.download.txt");
            ExtractManifestFile = ModuleRoot.Combine(context.PathTable, "manifest.extract.txt");

            ContentHash = contentHash;
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
        public AbsolutePath DownloadManifestFile { get; }

        /// <nodoc />
        public AbsolutePath ExtractManifestFile { get; }

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
    }
}

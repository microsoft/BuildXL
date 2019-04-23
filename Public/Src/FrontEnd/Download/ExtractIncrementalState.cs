// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Download.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// State that contains information about an extracted folder
    /// </summary>
    public class ExtractIncrementalState
    {
        private const string ManifestVersion = "v2";

        private readonly DownloadData m_downloadData;

        /// <nodoc />
        public IReadOnlyDictionary<AbsolutePath, ContentHash> Hashes { get; }

        /// <nodoc />
        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> Files { get; }

        /// <nodoc />
        public ExtractIncrementalState(DownloadData downloadData, SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> files, Dictionary<AbsolutePath, ContentHash> hashes)
        {
            m_downloadData = downloadData;
            Files = files;
            Hashes = hashes;
        }

        /// <nodoc />
        public static async Task<ExtractIncrementalState> TryLoadAsync(Logger logger, FrontEndContext context, DownloadData downloadData)
        {
            var manifestFilePath = downloadData.ExtractManifestFile.ToString(context.PathTable);

            ExtractIncrementalState result = null;
            if (!FileUtilities.Exists(manifestFilePath))
            {
                return null;
            }

            using (var reader = new StreamReader(manifestFilePath))
            {
                var versionLine = await reader.ReadLineAsync();
                if (versionLine == null || !string.Equals(versionLine, ManifestVersion, StringComparison.Ordinal))
                {
                    logger.ExtractManifestDoesNotMatch(context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, "version", ManifestVersion, versionLine);
                    return null;
                }

                var urlLine = await reader.ReadLineAsync();
                if (!string.Equals(urlLine, downloadData.Settings.Url, StringComparison.Ordinal))
                {
                    logger.ExtractManifestDoesNotMatch(context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, "url", downloadData.Settings.Url, urlLine);
                    return null;
                }

                var archiveTypeLine = await reader.ReadLineAsync();
                if (archiveTypeLine == null || !Enum.TryParse<DownloadArchiveType>(archiveTypeLine, out var archiveType) || archiveType != downloadData.Settings.ArchiveType)
                {
                    logger.ExtractManifestDoesNotMatch(context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, "archiveType", downloadData.Settings.ArchiveType.ToString(), archiveTypeLine);
                    return null;
                }

                var fileCountLine = await reader.ReadLineAsync();
                if (fileCountLine == null || !uint.TryParse(fileCountLine, out var fileCount))
                {
                    return null;
                }

                var hashes = new Dictionary<AbsolutePath, ContentHash>();
                var files = new FileArtifact[fileCount];
                for (int i = 0; i < fileCount; i++)
                {
                    var filePathLine = await reader.ReadLineAsync();
                    if (filePathLine == null || !RelativePath.TryCreate(context.StringTable, filePathLine, out var relativeFilePath))
                    {
                        return null;
                    }

                    var hashLine = await reader.ReadLineAsync();
                    if (hashLine == null || !ContentHash.TryParse(hashLine, out var contentHash))
                    {
                        return null;
                    }

                    var filePath = downloadData.ContentsFolder.Path.Combine(context.PathTable, relativeFilePath);
                    files[i] = FileArtifact.CreateSourceFile(filePath);
                    hashes[filePath] = contentHash;
                }

                var sortedFiles = SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.SortUnsafe(
                    files,
                    OrdinalFileArtifactComparer.Instance);

                result = new ExtractIncrementalState(downloadData, sortedFiles, hashes);
            }

            return result;
        }

        /// <nodoc />
        public Task SaveAsync(FrontEndContext context)
        {
            // We have to write a hash file for this extraction
            return FileUtilities.WriteAllTextAsync(
                m_downloadData.ExtractManifestFile.ToString(context.PathTable),
                string.Join(
                    Environment.NewLine,
                    ManifestVersion,
                    m_downloadData.Settings.Url,
                    m_downloadData.Settings.ArchiveType,
                    Hashes.Count.ToString(CultureInfo.InvariantCulture),
                    string.Join(Environment.NewLine, Hashes.Select(
                        kv =>
                        {
                            var success = m_downloadData.ContentsFolder.Path.TryGetRelative(context.PathTable, kv.Key, out var relativePath);
                            Contract.Assert(success, "Enumeration should have resulted in a relative path here....");

                            return $"{relativePath.ToString(context.StringTable)}{Environment.NewLine}{kv.Value.ToString()}";

                        }))
                ),
                System.Text.Encoding.UTF8);
        }
    }
}

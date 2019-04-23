// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.FrontEnd.Download.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Native.IO;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// Data that encapsulates information whether a download needs to happen or can be skipped
    /// </summary>
    public class DownloadIncrementalState
    {
        private const string ManifestVersion = "v2";

        private readonly DownloadData m_downloadData;

        /// <nodoc />
        public ContentHash ContentHash { get; }

        /// <nodoc />
        public DownloadIncrementalState(DownloadData downloadData, ContentHash contentHash)
        {
            m_downloadData = downloadData;
            ContentHash = contentHash;
        }

        /// <nodoc />
        public static async Task<DownloadIncrementalState> TryLoadAsync(Logger logger, FrontEndContext context, DownloadData downloadData)
        {
            var manifestFilePath = downloadData.DownloadManifestFile.ToString(context.PathTable);

            DownloadIncrementalState result = null;
            if (!FileUtilities.Exists(manifestFilePath))
            {
                return null;
            }

            using (var reader = new StreamReader(manifestFilePath))
            {
                var versionLine = await reader.ReadLineAsync();
                if (!string.Equals(versionLine, ManifestVersion, StringComparison.Ordinal))
                {
                    logger.DownloadManifestDoesNotMatch(context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, "versionLine", ManifestVersion, versionLine);
                    return null;
                }

                var urlLine = await reader.ReadLineAsync();
                if (!string.Equals(urlLine, downloadData.Settings.Url, StringComparison.Ordinal))
                {
                    logger.DownloadManifestDoesNotMatch(context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, "url", downloadData.Settings.Url, urlLine);
                    return null;
                }
                
                var fileNameLine = await reader.ReadLineAsync();
                if (!string.Equals(fileNameLine, downloadData.Settings.FileName, StringComparison.Ordinal))
                {
                    logger.DownloadManifestDoesNotMatch(context.LoggingContext, downloadData.Settings.ModuleName, downloadData.Settings.Url, "fileName", downloadData.Settings.FileName, fileNameLine);
                    return null;
                }
                
                var hashLine = await reader.ReadLineAsync();
                if (hashLine == null || !ContentHash.TryParse(hashLine, out var expectedHash))
                {
                    return null;
                }

                result = new DownloadIncrementalState(downloadData, expectedHash);
            }

            return result;
        }

        /// <nodoc />
        public Task SaveAsync(FrontEndContext context)
        {
            // We have to write a hash file for this download
            return FileUtilities.WriteAllTextAsync(
                m_downloadData.DownloadManifestFile.ToString(context.PathTable),
                string.Join(
                    Environment.NewLine,
                    ManifestVersion,
                    m_downloadData.Settings.Url,
                    m_downloadData.Settings.FileName,
                    ContentHash.ToString()
                ),
                System.Text.Encoding.UTF8);
        }
    }
}

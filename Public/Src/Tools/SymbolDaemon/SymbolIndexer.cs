// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Symbol.Common;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Straightforward implementation of ISymbolIndexer
    /// </summary>
    public class SymbolIndexer : ISymbolIndexer
    {
        private readonly SymstoreUtil m_symstoreUtil;
        private readonly Microsoft.VisualStudio.Services.Symbol.App.Indexer.IndexerUtil m_indexerUtil;

        /// <nodoc />
        public SymbolIndexer(IAppTraceSource tracer)
        {
            m_symstoreUtil = new SymstoreUtil(tracer);
            m_symstoreUtil.SetIndexableFileFormats(SymbolFileFormat.All);
            m_indexerUtil = new Microsoft.VisualStudio.Services.Symbol.App.Indexer.IndexerUtil(NoopAppTraceSource.Instance);
            m_indexerUtil.SetIndexableFileFormats(Microsoft.VisualStudio.Services.Symbol.App.Indexer.SymbolFileFormat.All);
        }

        /// <inheritdoc/>
        public IEnumerable<DebugEntryData> GetDebugEntries(FileInfo file, bool calculateBlobId = false)
        {
            Contract.Requires(File.Exists(file.FullName));

            List<DebugEntryData> entries = new();
            entries.AddRange(m_symstoreUtil.GetDebugEntryData(
                file.FullName,
                new[] { file.FullName },
                calculateBlobId,
                // Currently, only file-deduped (VsoHash) symbols are supported.
                isChunked: false));

            // If we get no entries from the symstore, it might mean that the file is not supported by the symstore.
            // In that case, we try to get entries using the IndexerUtil. The symstore is used first because it always
            // provides correct value for the <see cref="IDebugEntryData.InformationLevel"/>. IndexerUtil assumes that 
            // all pdbs contain 'private-level' symbol data. Even though IndexerUtil supports more file formats, switching
            // to it as the first option would break the current behavior of the SymbolDaemon.
            if (entries.Count == 0)
            {
                // IndexerUtil opens files with FileAccess.ReadWrite. Files that are hardlinked from cache cannot be opened
                // with such FileAccess level, i.e., IndexerUtil fails with AccessDenied. As a temporary workaround (until
                // FileAccess level in IndexerUtil is fixed), we create a copy of the file and run the indexer on that copy.
                string tempFileName = FileUtilities.GetTempFileName();
                try
                {
                    if (!FileUtilities.CopyFileAsync(file.FullName, tempFileName).ConfigureAwait(false).GetAwaiter().GetResult())
                    {
                        throw new IOException($"Failed to copy file '{file.FullName}' to '{tempFileName}'.");
                    }

                    entries.AddRange(m_indexerUtil.GetDebugEntryData(
                        tempFileName,
                        // We need to pass the original file name to the IndexerUtil, because it uses it to create the client key.
                        new[] { file.FullName },
                        calculateBlobId,
                        // Currently, only file-deduped (VsoHash) symbols are supported
                        isChunked: false));
                }
                finally
                {
                    // Clean up the temporary file.
                    if (File.Exists(tempFileName))
                    {
                        FileUtilities.DeleteFile(tempFileName);
                    }
                }
            }

            return entries;
        }
    }
}

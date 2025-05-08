// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
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
        private const string c_invalidClientKey = "/00000000000000000000000000000000/";

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
                isChunked: false)
                // SymstoreUtil can interpret a Linux debug symbol file as a .pdb file and attempt to extract a key out of it.
                // In such a case, the extraction will fail but it will be a silent failure and SymstoreUtil just returns a string of zeros.
                // We need to check and skip all entries with invalid keys.
                .Where(entry => entry.ClientKey.IndexOf(c_invalidClientKey) == -1));

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
                        // Currently, there is a bug in the key generation for debug symbols (file extension .dbg/.debug) in IndexerUtil.
                        // To work around it, in case of debug symbols, we don't pass the file name at all. It's fine to do this because
                        // the clientkeys for debug symbols do not include file name, they are always in _.debug\elf - build - sym - xxxx\_.debug format.
                        (file.FullName.EndsWith(".debug", StringComparison.OrdinalIgnoreCase) || file.FullName.EndsWith(".dbg", StringComparison.OrdinalIgnoreCase)) ? null : new[] { file.FullName },
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

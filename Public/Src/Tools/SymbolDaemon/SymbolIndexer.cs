// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.Symbol.Common;
using Newtonsoft.Json;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// Straightforward implementation of ISymbolIndexer
    /// </summary>
    public class SymbolIndexer : ISymbolIndexer
    {
        private readonly SymstoreUtil m_symstoreUtil;

        /// <nodoc />
        public SymbolIndexer(IAppTraceSource tracer)
        {
            m_symstoreUtil = new SymstoreUtil(tracer);
            m_symstoreUtil.SetIndexableFileFormats(SymbolFileFormat.All);
        }

        /// <inheritdoc/>
        public IEnumerable<DebugEntryData> GetDebugEntries(FileInfo file, bool calculateBlobId = false)
        {
            Contract.Requires(File.Exists(file.FullName));

            var entries = m_symstoreUtil.GetDebugEntryData(
                file.FullName,
                new[] { file.FullName },
                calculateBlobId,
                // Currently, only file-deduped (VsoHash) symbols are supported
                isChunked: false);

            return entries;
        }
    }
}

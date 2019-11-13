using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
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

        /// <nodoc />
        public SymbolIndexer(IAppTraceSource tracer)
        {
            m_symstoreUtil = new SymstoreUtil(tracer);
        }

        /// <inheritdoc/>
        public IEnumerable<DebugEntryData> GetDebugEntries(FileInfo file, bool calculateBlobId = false)
        {
            Contract.Requires(File.Exists(file.FullName));

            var entries = m_symstoreUtil.GetDebugEntryData(
                file.FullName,
                new[] { file.FullName },
                calculateBlobId);

            return entries;
        }
    }
}

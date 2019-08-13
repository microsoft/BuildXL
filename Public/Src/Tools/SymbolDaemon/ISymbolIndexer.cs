using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.Services.Symbol.Common;

namespace Tool.SymbolDaemon
{
    /// <nodoc />
    public interface ISymbolIndexer
    {
        /// <summary>
        /// Indexes the file and returns an enumeration of its DebugEntryDatas.
        /// </summary> 
        /// <param name="file">File to be indexed (must be present on disk).</param>
        /// <param name="calculateBlobId">If set to false, the file won't be hashed and <see cref="IDebugEntryData.BlobIdentifier"/> will be set to null</param>
        IEnumerable<DebugEntryData> GetDebugEntries(FileInfo file, bool calculateBlobId = false);
    }
}
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips
{
    /// <summary>
    /// Stores serialized pips and the metadata required to query them without hydration.
    /// </summary>
    public interface IPipTable : IDisposable
    {
        /// <summary>
        /// Gets whether the table has been disposed.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Gets whether the persisted representation must remain uncompressed so it can be accessed at arbitrary byte offsets.
        /// </summary>
        bool RequiresUncompressedSerialization { get; }

        /// <summary>
        /// Gets the number of pip reads.
        /// </summary>
        long Reads { get; }

        /// <summary>
        /// Gets the number of pip writes.
        /// </summary>
        int Writes { get; }

        /// <summary>
        /// Gets read counts grouped by query context.
        /// </summary>
        IEnumerable<KeyValuePair<PipQueryContext, int>> DeserializationContexts { get; }

        /// <summary>
        /// Gets the number of pips in the table.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Gets pip identifiers in stable insertion order.
        /// </summary>
        IList<PipId> StableKeys { get; }

        /// <summary>
        /// Gets all pip identifiers.
        /// </summary>
        IEnumerable<PipId> Keys { get; }

        /// <summary>
        /// Gets the number of backing page streams.
        /// </summary>
        int PageStreamsCount { get; }

        /// <summary>
        /// How many bytes do the buffers of the streams occupy in memory.
        /// </summary>
        long Size { get; }

        /// <summary>
        /// Gets elapsed pip-write time in milliseconds.
        /// </summary>
        long WritesMilliseconds { get; }

        /// <summary>
        /// Gets elapsed pip-read time in milliseconds.
        /// </summary>
        long ReadsMilliseconds { get; }

        /// <summary>
        /// How many bytes of the stream buffers are actually used.
        /// </summary>
        long Used { get; }

        /// <summary>
        /// Gets the number of pips currently retained by weak references.
        /// </summary>
        int Alive { get; }

        /// <summary>
        /// Adds a pip and returns its identifier.
        /// </summary>
        PipId Add(uint nodeIdValue, Pip pip);

        /// <summary>
        /// Returns whether a pip identifier belongs to this table.
        /// </summary>
        bool IsValid(PipId pipId);

        #region Pip metadata

        /// <summary>
        /// Gets the pip type without hydrating the pip.
        /// </summary>
        PipType GetPipType(PipId pipId);

        /// <summary>
        /// Gets process options without hydrating the pip.
        /// </summary>
        Process.Options GetProcessOptions(PipId pipId);

        /// <summary>
        /// Gets the process rewrite policy without hydrating the pip.
        /// </summary>
        RewritePolicy GetRewritePolicy(PipId pipId);

        /// <summary>
        /// Gets the process executable path without hydrating the pip.
        /// </summary>
        AbsolutePath GetProcessExecutablePath(PipId pipId);

        /// <summary>
        /// Gets the seal directory kind without hydrating the pip.
        /// </summary>
        SealDirectoryKind GetSealDirectoryKind(PipId pipId);

        /// <summary>
        /// Gets the seal directory root without hydrating the pip.
        /// </summary>
        AbsolutePath GetSealDirectoryRoot(PipId pipId);

        /// <summary>
        /// Gets whether the seal directory should be scrubbed.
        /// </summary>
        bool ShouldScrubFullSealDirectory(PipId pipId);

        /// <summary>
        /// Gets whether the seal directory is composite.
        /// </summary>
        bool IsSealDirectoryComposite(PipId pipId);

        /// <summary>
        /// Gets source seal directory patterns without hydrating the pip.
        /// </summary>
        ReadOnlyArray<StringId> GetSourceSealDirectoryPatterns(PipId pipId);

        /// <summary>
        /// Gets the pip semistable hash without hydrating the pip.
        /// </summary>
        long GetPipSemiStableHash(PipId pipId);

        /// <summary>
        /// Gets the pip scheduling priority.
        /// </summary>
        int GetPipPriority(PipId pipId);

        /// <summary>
        /// Gets the formatted pip semistable hash without hydrating the pip.
        /// </summary>
        string GetFormattedSemiStableHash(PipId pipId);

        /// <summary>
        /// Gets service information without hydrating the pip.
        /// </summary>
        ServiceInfo GetServiceInfo(PipId pipId);

        /// <summary>
        /// Gets the owning process module without hydrating the pip.
        /// </summary>
        ModuleId GetProcessModuleId(PipId pipId);

        /// <summary>
        /// Gets the process tool description without hydrating the pip.
        /// </summary>
        StringId GetProcessToolDescription(PipId pipId);

        /// <summary>
        /// Gets the process qualifier without hydrating the pip.
        /// </summary>
        QualifierId GetProcessQualifierId(PipId pipId);

        /// <summary>
        /// Gets the process weight without hydrating the pip.
        /// </summary>
        int GetProcessWeight(PipId pipId);

        /// <summary>
        /// Gets the process file dependency count without hydrating the pip.
        /// </summary>
        int GetProcessFileDependencyCount(PipId pipId);

        /// <summary>
        /// Gets the process directory dependency count without hydrating the pip.
        /// </summary>
        int GetProcessDirectoryDependencyCount(PipId pipId);

        /// <summary>
        /// Gets the process file output count without hydrating the pip.
        /// </summary>
        int GetProcessFileOutputCount(PipId pipId);

        /// <summary>
        /// Gets the process directory output count without hydrating the pip.
        /// </summary>
        int GetProcessDirectoryOutputCount(PipId pipId);

        /// <summary>
        /// Gets whether the process should fail the build immediately.
        /// </summary>
        bool IsSucceedFast(PipId pipId);

        /// <summary>
        /// Gets whether outputs must remain writable.
        /// </summary>
        bool MustOutputsRemainWritable(PipId pipId);

        /// <summary>
        /// Gets whether the pip has preserve-outputs semantics.
        /// </summary>
        bool IsPreservedOutputsPip(PipId pipId);

        /// <summary>
        /// Gets whether the pip is an incremental tool.
        /// </summary>
        bool IsIncrementalTool(PipId pipId);

        /// <summary>
        /// Gets whether the process has a preserve-output allowlist.
        /// </summary>
        bool HasPreserveOutputAllowlist(PipId pipId);

        /// <summary>
        /// Gets the process preserve-output trust level.
        /// </summary>
        int GetProcessPreserveOutputsTrustLevel(PipId pipId);

        #endregion Pip metadata

        /// <summary>
        /// Hydrates and returns a pip body.
        /// </summary>
        Pip HydratePip(PipId pipId, PipQueryContext context);

        /// <summary>
        /// Stops accepting background serialization work.
        /// </summary>
        void StopBackgroundSerialization();

        /// <summary>
        /// Returns a task that completes when pending serialization and storage finalization finish.
        /// </summary>
        Task WhenDone();

        /// <summary>
        /// Stops and completes pending serialization, then writes the table to the provided writer.
        /// </summary>
        void Serialize(BuildXLWriter writer, int maxDegreeOfParallelism);
    }

    /// <summary>
    /// Deserializes pip table implementations.
    /// </summary>
    public static class PipTableFactory
    {
        /// <summary>
        /// Deserializes either the eager or file-backed pip table format.
        /// </summary>
        public static async Task<IPipTable> DeserializeAsync(
            BuildXLReader reader,
            Task<PathTable> pathTableTask,
            Task<SymbolTable> symbolTableTask,
            int initialBufferSize,
            int maxDegreeOfParallelism,
            bool debug)
        {
            Contract.RequiresNotNull(reader);
            Contract.RequiresNotNull(pathTableTask);
            Contract.RequiresNotNull(symbolTableTask);

            if (reader.BaseStream.CanSeek && FileBackedPipTable.IsFileBackedFormat(reader.BaseStream))
            {
                return await FileBackedPipTable.DeserializeAsync(
                    reader,
                    pathTableTask,
                    symbolTableTask,
                    maxDegreeOfParallelism);
            }

            return await PipTable.DeserializeAsync(
                reader,
                pathTableTask,
                symbolTableTask,
                initialBufferSize,
                maxDegreeOfParallelism,
                debug);
        }
    }
}

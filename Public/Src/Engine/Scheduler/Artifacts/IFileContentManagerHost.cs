// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Scheduler.Artifacts
{
    /// <summary>
    /// Host services and state for <see cref="FileContentManager"/>
    /// </summary>
    public interface IFileContentManagerHost
    {
        /// <summary>
        /// Context used for executing pips.
        /// </summary>
        PipExecutionContext Context { get; }

        /// <summary>
        /// Gets the execution logging context
        /// </summary>
        LoggingContext LoggingContext { get; }

        /// <summary>
        /// The Configuration.
        /// </summary>
        /// <remarks>
        /// Ideally this is only ISandBoxConfiguration, but have to expose a larger config object for now due to existing tangling.
        /// </remarks>
        IConfiguration Configuration { get; }

        /// <summary>
        /// Representation of local disks, allowing storage and retrieval of content at particular paths.
        /// This store is responsible for tracking changes to paths that are accessed (including remembering
        /// their hashes to avoid re-hashing or re-materializing them).
        /// </summary>
        LocalDiskContentStore LocalDiskContentStore { get; }

        /// <summary>
        /// Storage of artifact content - a content-addressable store.
        /// </summary>
        IArtifactContentCache ArtifactContentCache { get; }

        /// <summary>
        /// The execution log
        /// </summary>
        IExecutionLogTarget ExecutionLog { get; }

        /// <summary>
        /// The expander for getting semantic path info
        /// </summary>
        SemanticPathExpander SemanticPathExpander { get; }

        /// <summary>
        /// Gets the seal directory kind for the given directory artifact
        /// </summary>
        SealDirectoryKind GetSealDirectoryKind(DirectoryArtifact directory);

        /// <summary>
        /// Gets the scrub flag of a seal directory
        /// </summary>
        bool ShouldScrubFullSealDirectory(DirectoryArtifact directory);

        /// <summary>
        /// Gets the producer pip for the given file
        /// </summary>
        Pip GetProducer(in FileOrDirectoryArtifact artifact);

        /// <summary>
        /// Attempts to get the producer id. Returns invalid if artifact is not a declared artifact
        /// </summary>
        PipId TryGetProducerId(in FileOrDirectoryArtifact artifact);

        /// <summary>
        /// Gets the pip description for the producer of the given file artifact
        /// </summary>
        string GetProducerDescription(in FileOrDirectoryArtifact artifact);

        /// <summary>
        /// Gets the pip description for a consumer of the given file artifact (if any). Otherwise, null.
        /// </summary>
        string GetConsumerDescription(in FileOrDirectoryArtifact artifact);

        /// <summary>
        /// Gets the static contents for the given directory artifact
        /// </summary>
        SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ListSealDirectoryContents(DirectoryArtifact directory);

        /// <summary>
        /// Gets whether the given file can be made readonly
        /// </summary>
        bool AllowArtifactReadOnly(in FileOrDirectoryArtifact artifact);

        /// <summary>
        /// Gets whether the artifact is an output that should be preserved.
        /// </summary>
        bool IsPreservedOutputArtifact(in FileOrDirectoryArtifact artifact);

        /// <summary>
        /// Callback to host to report output content
        /// </summary>
        void ReportContent(FileArtifact artifact, in FileMaterializationInfo trackedFileContentInfo, PipOutputOrigin origin);

        /// <summary>
        /// Callback to host to report materialized artifact.
        /// </summary>
        void ReportMaterializedArtifact(in FileOrDirectoryArtifact artifact);

        /// <summary>
        /// Gets whether the host supports materializing the given file
        /// </summary>
        bool CanMaterializeFile(FileArtifact artifact);

        /// <summary>
        /// Attempts to get the source file a copy file (if the file is a copy file output)
        /// </summary>
        bool TryGetCopySourceFile(FileArtifact artifact, out FileArtifact sourceFile);

        /// <summary>
        /// Callback to materialize a file if the host supports materializing the given file
        /// </summary>
        /// <returns>True if the file was materialize by the host. Failure if host supports materializing the file and failed during materialization.</returns>
        Task<Possible<ContentMaterializationOrigin>> TryMaterializeFileAsync(FileArtifact artifact, OperationContext operationContext);
    }
}

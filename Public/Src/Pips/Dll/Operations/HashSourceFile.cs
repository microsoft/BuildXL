// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// A <see cref="HashSourceFile" /> pip is a scheduler-internal pip representing the schedulable work of hashing a source
    /// artifact.
    /// </summary>
    /// <remarks>
    /// Dependent pips cannot run until a source file has been hashed. We could track source files separately and have a count
    /// on each real pip of how many
    /// source files still need to be hashed for it to be scheduled, and for each source file record temporarily those
    /// dependent pips (to notify them after hashing).
    /// That turns out to be the same structure as any other pip-to-pip dependency.
    ///
    /// Furthermore, the existence of these pips gives the property that every artifact in the build has exactly one producer.
    /// This is a very useful for incremental / journal-based scheduling: Given a changed file, we find the producer and then walk the graph from there.
    /// </remarks>
    public sealed class HashSourceFile : Pip
    {
        /// <summary>
        /// The source artifact represented by this pip. On completion, the scheduler has a content hash for this artifact.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public FileArtifact Artifact { get; }

        /// <summary>
        /// Creates a pip representing the hashing of the given source artifact.
        /// </summary>
        public HashSourceFile(FileArtifact sourceFile)
        {
            Contract.Requires(sourceFile.IsValid);
            Contract.Requires(sourceFile.IsSourceFile);

            Artifact = sourceFile;
        }

        /// <inheritdoc />
        public override ReadOnlyArray<StringId> Tags => default(ReadOnlyArray<StringId>);

        /// <inheritdoc />
        public override PipProvenance Provenance => null;

        /// <inheritdoc />
        public override PipType PipType => PipType.HashSourceFile;

        #region Serialization

        /// <nodoc />
        internal static HashSourceFile InternalDeserialize(PipReader reader)
        {
            return new HashSourceFile(
                reader.ReadFileArtifact());
        }

        /// <inheritdoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            writer.Write(Artifact);
        }
        #endregion
    }
}

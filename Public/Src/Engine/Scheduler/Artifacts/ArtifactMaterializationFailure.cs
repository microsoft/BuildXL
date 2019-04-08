// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Artifacts
{
    /// <summary>
    /// Represents a failure in file or directory materialization with associated message (something describing the why or how of the failure).
    /// </summary>
    public sealed class ArtifactMaterializationFailure : Failure
    {
        private const int MaximumFailedFileCount = 5;

        private readonly ReadOnlyArray<(FileArtifact, ContentHash)> m_failedFiles;
        private readonly ReadOnlyArray<DirectoryArtifact> m_failedDirectories;
        private readonly PathTable m_pathTable;

        /// <nodoc />
        public ArtifactMaterializationFailure(ReadOnlyArray<(FileArtifact, ContentHash)> failedFiles, PathTable pathTable, Failure innerFailure = null)
            : this(failedFiles, ReadOnlyArray<DirectoryArtifact>.Empty, pathTable, innerFailure)
        {
        }

        /// <nodoc />
        public ArtifactMaterializationFailure(ReadOnlyArray<(FileArtifact, ContentHash)> failedFiles, ReadOnlyArray<DirectoryArtifact> failedDirectories, PathTable pathTable, Failure innerFailure = null)
            : base(innerFailure)
        {
            Contract.Requires(failedFiles.Length > 0 || failedDirectories.Length > 0);
            m_failedFiles = failedFiles;
            m_pathTable = pathTable;
            m_failedDirectories = failedDirectories;
        }

        /// <inheritdoc />
        public override BuildXLException CreateException()
        {
            return new BuildXLException(I($"Failure: {DescribeIncludingInnerFailures()}"));
        }

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return m_failedFiles.Length > 0 ? DescribeFilesFailure() : DescribeDirectoriesFailure();
        }

        private string DescribeFilesFailure()
        {
            string additionalFailureMessage = m_failedFiles.Length > MaximumFailedFileCount ?
                I($" (+ {m_failedFiles.Length - MaximumFailedFileCount} more)") : string.Empty;
            return string.Join(
                Environment.NewLine,
                new[] { I($"Failed files{additionalFailureMessage} [see warning log for details]:") }
                .Concat(m_failedFiles.Take(MaximumFailedFileCount).Select(f => I($"{f.Item1.Path.ToString(m_pathTable)} | Hash={f.Item2.ToString()}"))));
        }

        private string DescribeDirectoriesFailure()
        {
            string additionalFailureMessage = m_failedDirectories.Length > MaximumFailedFileCount ?
                I($" (+ {m_failedDirectories.Length - MaximumFailedFileCount} more)") : string.Empty;
            return string.Join(
                Environment.NewLine,
                new[] { I($"Failed directories{additionalFailureMessage} [see warning log for details]:") }
                .Concat(m_failedDirectories.Take(MaximumFailedFileCount).Select(f => f.Path.ToString(m_pathTable))));
        }
    }
}

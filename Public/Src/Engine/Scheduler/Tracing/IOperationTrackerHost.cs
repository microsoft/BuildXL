// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Host for <see cref="OperationTracker"/> enables descriptions for operations
    /// </summary>
    public interface IOperationTrackerHost
    {
        /// <summary>
        /// Gets description for the artifact
        /// </summary>
        string GetDescription(in FileOrDirectoryArtifact artifact);

        /// <summary>
        /// Gets description for the artifact
        /// </summary>
        string GetDescription(PipId pipId);
    }

    /// <summary>
    /// Null operation tracker host (for testing use only)
    /// </summary>
    internal sealed class NullOperationTrackerHost : IOperationTrackerHost
    {
        /// <inheritdoc />
        public string GetDescription(PipId pipId)
        {
            return null;
        }

        /// <inheritdoc />
        public string GetDescription(in FileOrDirectoryArtifact artifact)
        {
            return null;
        }
    }
}

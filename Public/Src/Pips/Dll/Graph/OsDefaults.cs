// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// The default untracked directories and files for a given OS
    /// </summary>
    public interface OsDefaults
    {
        /// <nodoc/>
        DirectoryArtifact[] UntrackedDirectories { get; }

        /// <nodoc/>
        public FileArtifact[] UntrackedFiles { get; }
    }
}
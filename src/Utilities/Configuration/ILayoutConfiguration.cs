// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Configures how things are layed out.
    /// </summary>
    public partial interface ILayoutConfiguration
    {
        /// <summary>
        /// The primary config file
        /// </summary>
        AbsolutePath PrimaryConfigFile { get; }

        /// <summary>
        /// The output directory
        /// </summary>
        AbsolutePath SourceDirectory { get; }

        /// <summary>
        /// The output directory
        /// </summary>
        AbsolutePath OutputDirectory { get; }

        /// <summary>
        /// The object directory
        /// </summary>
        AbsolutePath ObjectDirectory { get; }
        
        /// <summary>
        /// The frontend directory
        /// </summary>
        AbsolutePath FrontEndDirectory { get; }

        /// <summary>
        /// The Cache Directory
        /// </summary>
        AbsolutePath CacheDirectory { get; }

        /// <summary>
        /// The Cache Directory
        /// </summary>
        AbsolutePath EngineCacheDirectory { get; }

        /// <summary>
        /// The temp directory
        /// </summary>
        AbsolutePath TempDirectory { get; }

        /// <summary>
        /// The absolute path of the folder where the BuildXL binaries that are currently being used are located
        /// </summary>
        /// <remarks>
        /// Will always be set by the engine.
        /// </remarks>
        AbsolutePath BuildXlBinDirectory { get; }

        /// <summary>
        /// Path to file containing known hashes for files.
        /// </summary>
        AbsolutePath FileContentTableFile { get; }

        /// <summary>
        /// Path to file defining symlinks
        /// </summary>
        AbsolutePath SymlinkDefinitionFile { get; }

        /// <summary>
        /// File change tracker file.
        /// </summary>
        AbsolutePath SchedulerFileChangeTrackerFile { get; }

        /// <summary>
        /// Incremental scheduling state file.
        /// </summary>
        AbsolutePath IncrementalSchedulingStateFile { get; }

        /// <summary>
        /// The fingerprint store directory.
        /// </summary>
        AbsolutePath FingerprintStoreDirectory { get; }
    }
}

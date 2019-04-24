// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// White List entry
    /// </summary>
    public partial interface IExportConfiguration
    {
        /// <summary>
        /// When enabled, captures build inputs needed for evaluation or the full build depending on the specified snapshot mode (see /snapshotMode)
        /// </summary>
        AbsolutePath SnapshotFile { get; }

        /// <summary>
        /// Specifies the mode used to snapshot the build: None, Full (capture all build inputs in VHD file) Evaluation (captures build specifications in zip file). Default is Evaluation.
        /// </summary>
        SnapshotMode SnapshotMode { get; }
    }
}

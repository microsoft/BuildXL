// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Information about changed file id.
    /// </summary>
    public readonly struct ChangedFileIdInfo
    {
        /// <summary>
        /// File id and volume id.
        /// </summary>
        public readonly FileIdAndVolumeId FileIdAndVolumeId;

        /// <summary>
        /// Usn record containing change reason.
        /// </summary>
        public readonly UsnRecord UsnRecord;

        /// <summary>
        /// Last tracked Usn by file change tracker.
        /// </summary>
        public readonly Usn? LastTrackedUsn;

        /// <summary>
        /// Creates an instance of <see cref="ChangedFileIdInfo" />.
        /// </summary>
        public ChangedFileIdInfo(FileIdAndVolumeId fileIdAndVolumeId, UsnRecord usnRecord, Usn? lastTrackedUsn = default)
        {
            FileIdAndVolumeId = fileIdAndVolumeId;
            UsnRecord = usnRecord;
            LastTrackedUsn = lastTrackedUsn;
        }
    }
}

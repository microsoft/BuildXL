// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// Creates an instance of <see cref="ChangedFileIdInfo" />.
        /// </summary>
        public ChangedFileIdInfo(FileIdAndVolumeId fileIdAndVolumeId, UsnRecord usnRecord)
        {
            FileIdAndVolumeId = fileIdAndVolumeId;
            UsnRecord = usnRecord;
        }
    }
}

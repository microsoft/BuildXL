// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Storage.ChangeTracking
{
    /// <summary>
    /// Records for tracking file changes.
    /// </summary>
    internal sealed class FileChangeTrackingRecord
    {
        /// <summary>
        /// USN.
        /// </summary>
        public readonly Usn Usn;

        /// <summary>
        /// File path.
        /// </summary>
        public readonly AbsolutePath Path;

        /// <summary>
        /// Next record.
        /// </summary>
        public FileChangeTrackingRecord Next;

        /// <nodoc />
        public FileChangeTrackingRecord(Usn usn, AbsolutePath path, FileChangeTrackingRecord next = null)
        {
            Usn = usn;
            Path = path;
            Next = next;
        }
    }
}

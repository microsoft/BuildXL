// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Enumeration representing the types of values that can be contained in a fragment.
    /// </summary>
    public enum PipFragmentType : byte
    {
        /// <summary>
        /// Invalid, default value.
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// A string literal.
        /// </summary>
        StringLiteral,

        /// <summary>
        /// Absolute file or directory path.
        /// </summary>
        AbsolutePath,

        /// <summary>
        /// VSO hash of a file.
        /// </summary>
        /// <remarks>
        /// This fragment type is needed so that we can keep all drop uploading business outside
        /// of BuildXL (i.e., implement it as an external pip) and still make it efficient.
        ///
        /// Lets say DropDaemon is an external process in charge of adding files (which are typically
        /// build artifacts) to drop.  For a file to be added to drop, DropDaemon first calls “Associate”
        /// for that file against the drop service endpoint. This operation requires the VSO hash of the
        /// file. The “Associate” operation returns whether the file already exists in the remote drop
        /// endpoint; if it does, it just associates that file with the drop name and we are done; if it
        /// doesn’t, DropDaemon needs to read the file from disk and upload it to the drop.
        ///
        /// This is a typical journey of a file before it becomes a part of a drop
        ///                                              __________
        /// +-----+                  +------------+     /          \
        /// | csc |---> file.dll --->| DropDaemon |--->| drop cloud |
        /// +-----+                  +------------+     \__________/
        ///
        /// If executing “csc” was a cache hit, then 'file.dll' didn't have to be materialized on disk
        /// (due to lazy materialization). Next, running “associate” on it (by DropDaemon) only requires
        /// the VSO hash of the file; if BuildXL can provide that VSO hash to DropDaemon, the file
        /// still doesn't have to be materialized on disk.  Finally, if “associate” returned true
        /// (meaning the file already exists in the drop cloud), we are done without ever having to
        /// materialize 'file.dll'.
        ///
        /// Large builds will typically have a lot of cache hits, so this optimization is deemed essential.
        /// </remarks>
        VsoHash,

        /// <summary>
        /// A nested description fragment.
        /// </summary>
        NestedFragment,

        /// <summary>
        /// IPC moniker (<see cref="BuildXL.Ipc.Interfaces.IIpcMoniker"/> to be rendered dynamically, before the pip is executed.
        /// </summary>
        IpcMoniker,

        /// <summary>
        /// File id, i.e., path and its rewrite count.
        /// </summary>
        FileId,
    }
}

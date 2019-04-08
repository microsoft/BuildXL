// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Defines the state that a file passed to AddToCas or ProduceFile will be in
    /// once the cache is done wtih it.
    /// </summary>
    /// <remarks>
    /// How the cache implements writeable or readonly will be specific to the cache
    /// and is intentionally undefined to the build engine. (i.e., hard links, file copy,
    /// copy on write, whatever)
    /// </remarks>
    public enum FileState
    {
        /// <summary>
        /// The file may be read only to the build engine and tools after the operation completes.
        /// </summary>
        /// <remarks>
        /// This does not gaurentee that writes will be blocked, only that the build engine will accept
        /// a read only copy of the file should the cache system produce / modify the new file.
        ///
        /// A Cache making the file ReadOnly will need to allow Delete of the file to still occur.
        /// </remarks>
        ReadOnly,

        /// <summary>
        /// The file will be writeable by the build engine and tools after the operation completes.
        /// </summary>
        /// <remarks>
        /// This does not commit the build engine to change the file, but signals to the cache that the
        /// build engine reserves the right to do so.
        /// </remarks>
        Writeable,
    }
}

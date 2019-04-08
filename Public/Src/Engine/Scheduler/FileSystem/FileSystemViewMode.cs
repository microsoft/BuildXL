// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler.FileSystem
{
    /// <summary>
    /// Represents the particular type of file system queried/cached in <see cref="FileSystemView"/>
    /// </summary>
    public enum FileSystemViewMode : byte
    {
        /// <summary>
        /// The real local file system
        /// </summary>
        Real,

        /// <summary>
        /// In-memory virtual file system representing all output files/directories and parent directories in pip graph combined with dynamic files reported during a build.
        /// </summary>
        /// <remarks>
        /// Note: In a distributed build, this file system view might be different on different workers. 
        /// For example, PipA produces dynamic files, PipB consume those filed. If a worker runs neither
        /// PipA nor PipB, its Output file system will not contain those dynamic files.
        /// </remarks>
        Output,

        /// <summary>
        /// In-memory virtual file system representing all files/directories and parent directories in pip graph
        /// </summary>
        FullGraph,
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     Constants for limiting file system support
    /// </summary>
    public static class FileSystemConstants
    {
        /// <summary>
        /// Returns true if the current platform is a Windows platform.
        /// </summary>
        public static bool IsWindowsOS { get; } = Environment.OSVersion.Platform != PlatformID.MacOSX && Environment.OSVersion.Platform != PlatformID.Unix;

        /// <summary>
        ///     File attributes that are not supported.
        /// </summary>
        public const FileAttributes UnsupportedFileAttributes = FileAttributes.Archive
                                                                | FileAttributes.Compressed
                                                                | FileAttributes.Device
                                                                | FileAttributes.Encrypted
                                                                | FileAttributes.Hidden
                                                                | FileAttributes.IntegrityStream
                                                                | FileAttributes.NoScrubData
                                                                | FileAttributes.NotContentIndexed
                                                                | FileAttributes.Offline
                                                                | FileAttributes.ReparsePoint
                                                                | FileAttributes.SparseFile
                                                                | FileAttributes.System
                                                                | FileAttributes.Temporary;

        /// <summary>
        /// Long path prefix.
        /// </summary>
        public const string LongPathPrefix = @"\\?\";

        private const int MaxPathUnix = 1024;

        /// <summary>
        /// Maximum path length when long paths are not supported.
        /// </summary>
        public static readonly int MaxShortPath = IsWindowsOS ? 260 : MaxPathUnix;

        /// <summary>
        /// Maximum path length for \\?\ style paths.
        /// </summary>
        public static readonly int MaxLongPath = IsWindowsOS ? 32767 : MaxPathUnix;

        /// <summary>
        /// Maximum path length for directory.
        /// </summary>
        public static readonly int MaxDirectoryPath = IsWindowsOS ? 248 : MaxPathUnix;

        /// <summary>
        /// Maximum number of hard links a single file can have.
        /// </summary>
        public static readonly int MaxLinks = IsWindowsOS ? 1024 : ushort.MaxValue;

        /// <summary>
        /// Maximum path length.
        /// </summary>
        public static int MaxPath { get; } = LongPathsSupported ? MaxLongPath : MaxShortPath;

        /// <summary>
        /// Returns true if paths longer then 260 characters are supported.
        /// </summary>
        public static bool LongPathsSupported { get; } = GetLongPathSupport();

        private static bool GetLongPathSupport()
        {
            if (!IsWindowsOS)
            {
                // There is no distinction between short paths and long paths on unix platforms.
                // So this property returns false, because paths longer then MaxShortPath are not valid.
                return false;
            }

            string longString = new string('a', MaxShortPath + 1);
            try
            {
                string path = $@"{LongPathPrefix}c:\foo{longString}.txt";
                var directoryName = Path.GetDirectoryName(path);
                return true;

            }
            catch (PathTooLongException)
            {
                return false;
            }
        }

        // ReSharper disable once InconsistentNaming

        /// <summary>
        ///     Recommended size for all file reads and writes.
        /// </summary>
        /// <remarks>
        ///     Should be larger than FileStreamBufferSize so that async I/O is consistently async.
        ///     There is a bug in FileStream in the BCL where the I/O completion port threads end up making blocking I/O.
        ///     This causes deadlocks. We avoid this by neutering FileStream's internal buffer by making I/O requests that
        ///     are larger than this internal buffer.  This avoid the bug.
        /// </remarks>
        public const int FileIOBufferSize = 64 * 1024;

        /// <summary>
        ///     File modes that are not supported.
        /// </summary>
        public static readonly IReadOnlyCollection<FileMode> UnsupportedFileModes = new[] { FileMode.Truncate, FileMode.Append };
    }
}

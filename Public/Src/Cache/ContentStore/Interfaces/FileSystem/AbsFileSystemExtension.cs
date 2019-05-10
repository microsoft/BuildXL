// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Contains a set of extension methods for <see cref="IAbsFileSystem"/> interface.
    /// </summary>
    public static class AbsFileSystemExtension
    {
        /// <summary>
        ///     Size of the buffer for FileStreams opened by this class
        /// </summary>
        public const int DefaultFileStreamBufferSize = 4 * 1024;

        /// <summary>
        /// Creates an empty file at a given path.
        /// </summary>
        public static async Task CreateEmptyFileAsync(this IAbsFileSystem fileSystem, AbsolutePath path)
        {
            fileSystem.CreateDirectory(path.Parent);
            using (await fileSystem.OpenAsync(path, FileAccess.Write, FileMode.Create, FileShare.None, FileOptions.None, bufferSize: 1).ConfigureAwait(false))
            {
            }
        }

        /// <summary>
        ///     Try getting the attributes of a file.
        /// </summary>
        /// <returns>Returns false if the file doesn't exist.</returns>
        public static bool TryGetFileAttributes(this IAbsFileSystem fileSystem, AbsolutePath path, out FileAttributes attributes)
        {
            if (!fileSystem.FileExists(path))
            {
                attributes = default;
                return false;
            }

            try
            {
                attributes = fileSystem.GetFileAttributes(path);
            }
            catch (FileNotFoundException)
            {
                // We checked file existence at the top of the method, but due to a race condition, the file can be gone
                // at the point when we tried getting attributes.
                // Current implementation tries it best to avoid unnecessary first chance exceptions, but can't eliminate them completely.
                attributes = default;
                return false;
            }
            
            return true;
        }

        /// <summary>
        /// Tries to read the content from a file <paramref name="absolutePath"/>.
        /// </summary>
        /// <returns>Returns <code>null</code> if file does not exist, or content of the file otherwise.</returns>
        /// <exception cref="Exception">Throws if the IO operation fails.</exception>
        public static async Task<string> TryReadFileAsync(this IAbsFileSystem fileSystem, AbsolutePath absolutePath, FileShare fileShare = FileShare.ReadWrite)
        {
            using (Stream readLockFile = await fileSystem.OpenAsync(
                absolutePath, FileAccess.Read, FileMode.Open, fileShare).ConfigureAwait(false))
            {
                if (readLockFile != null)
                {
                    using (var reader = new StreamReader(readLockFile))
                    {
                        return await reader.ReadToEndAsync().ConfigureAwait(false);
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Reads the content from a file <paramref name="absolutePath"/>.
        /// </summary>
        /// <exception cref="Exception">Throws if the IO operation fails.</exception>
        public static string ReadAllText(this IAbsFileSystem fileSystem, AbsolutePath absolutePath, FileShare fileShare = FileShare.ReadWrite)
        {
            using (Stream readLockFile = fileSystem.OpenSafeAsync(absolutePath, FileAccess.Read, FileMode.Open, fileShare).GetAwaiter().GetResult())
            {
                using (var reader = new StreamReader(readLockFile))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Writes the content to a file <paramref name="absolutePath"/>.
        /// </summary>
        /// <exception cref="Exception">Throws if the IO operation fails.</exception>
        public static void WriteAllText(this IAbsFileSystem fileSystem, AbsolutePath absolutePath, string contents, FileShare fileShare = FileShare.ReadWrite)
        {
            using (Stream file = fileSystem.OpenSafeAsync(absolutePath, FileAccess.Write, FileMode.Create, fileShare).GetAwaiter().GetResult())
            {
                using (var writer = new StreamWriter(file))
                {
                    writer.WriteLine(contents);
                }
            }
        }

        /// <summary>
        /// Open the named file asynchronously for reading.
        /// </summary>
        /// <exception cref="FileNotFoundException">Throws if the file is not found.</exception>
        /// <remarks>
        /// Unlike <see cref="IAbsFileSystem.OpenAsync(AbsolutePath, FileAccess, FileMode, FileShare, FileOptions, int)"/> this function throws an exception if the file is missing.
        /// </remarks>
        public static async Task<Stream> OpenSafeAsync(this IAbsFileSystem fileSystem, AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options = FileOptions.None, int bufferSize = DefaultFileStreamBufferSize)
        {
            var stream = await fileSystem.OpenAsync(path, fileAccess, fileMode, share, options, bufferSize);
            if (stream == null)
            {
                throw new FileNotFoundException($"The file '{path}' does not exist.");
            }

            return stream;
        }

        /// <summary>
        /// Open the named file asynchronously for reading.
        /// </summary>
        /// <exception cref="FileNotFoundException">Throws if the file is not found.</exception>
        /// <remarks>
        /// Unlike <see cref="IReadableFileSystem{T}.OpenReadOnlyAsync(T, FileShare)"/> this function throws an exception if the file is missing.
        /// </remarks>
        public static async Task<Stream> OpenReadOnlySafeAsync(this IAbsFileSystem fileSystem, AbsolutePath path, FileShare share)
        {
            var stream = await fileSystem.OpenReadOnlyAsync(path, share);
            if (stream == null)
            {
                throw new FileNotFoundException($"The file '{path}' does not exist.");
            }

            return stream;
        }

        /// <summary>
        /// Calls <see cref="IAbsFileSystem.OpenAsync(AbsolutePath, FileAccess, FileMode, FileShare, FileOptions, int)" /> with the default buffer stream size.
        /// </summary>
        public static Task<Stream> OpenAsync(this IAbsFileSystem fileSystem, AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share)
        {
            return fileSystem.OpenAsync(path, fileAccess, fileMode, share, FileOptions.None, DefaultFileStreamBufferSize);
        }
    }
}

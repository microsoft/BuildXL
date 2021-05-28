// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;

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
        public static int DefaultFileStreamBufferSize => FileSystemDefaults.DefaultFileStreamBufferSize;

        /// <summary>
        /// Creates an empty file at a given path.
        /// </summary>
        public static void CreateEmptyFile(this IAbsFileSystem fileSystem, AbsolutePath path)
        {
            Contract.RequiresNotNull(path);
            Contract.RequiresNotNull(path.Parent);

            fileSystem.CreateDirectory(path.Parent);
            using (fileSystem.TryOpen(path, FileAccess.Write, FileMode.Create, FileShare.None, FileOptions.None, bufferSize: 1))
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
        public static async Task<string?> TryReadFileAsync(this IAbsFileSystem fileSystem, AbsolutePath absolutePath, FileShare fileShare = FileShare.ReadWrite)
        {
            using Stream? readLockFile = fileSystem.TryOpen(absolutePath, FileAccess.Read, FileMode.Open, fileShare);
            if (readLockFile != null)
            {
                using var reader = new StreamReader(readLockFile);
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// Reads the content from a file <paramref name="absolutePath"/>.
        /// </summary>
        /// <exception cref="Exception">Throws if the IO operation fails.</exception>
        public static string ReadAllText(this IAbsFileSystem fileSystem, AbsolutePath absolutePath, FileShare fileShare = FileShare.ReadWrite)
        {
            using Stream readLockFile = fileSystem.Open(absolutePath, FileAccess.Read, FileMode.Open, fileShare);
            using var reader = new StreamReader(readLockFile);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Writes the content to a file <paramref name="absolutePath"/>.
        /// </summary>
        /// <exception cref="Exception">Throws if the IO operation fails.</exception>
        public static void WriteAllText(this IAbsFileSystem fileSystem, AbsolutePath absolutePath, string contents, FileShare fileShare = FileShare.ReadWrite)
        {
            using Stream file = fileSystem.Open(absolutePath, FileAccess.Write, FileMode.Create, fileShare);
            using var writer = new StreamWriter(file);
            writer.Write(contents);
        }

        /// <summary>
        /// Calls <see cref="IAbsFileSystem.OpenAsync(AbsolutePath, FileAccess, FileMode, FileShare, FileOptions, int)" /> with the default buffer stream size.
        /// </summary>
        public static StreamWithLength? TryOpen(this IAbsFileSystem fileSystem, AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share)
        {
            return fileSystem.TryOpen(path, fileAccess, fileMode, share, FileOptions.None, DefaultFileStreamBufferSize);
        }

        /// <summary>
        /// Open the named file asynchronously for reading.
        /// </summary>
        /// <exception cref="FileNotFoundException">Throws if the file is not found.</exception>
        /// <exception cref="DirectoryNotFoundException">Throws if the directory is not found.</exception>
        /// <remarks>
        /// Unlike <see cref="IAbsFileSystem.OpenAsync(AbsolutePath, FileAccess, FileMode, FileShare, FileOptions, int)"/> this function throws an exception if the file is missing.
        /// </remarks>
        public static StreamWithLength Open(this IAbsFileSystem fileSystem, AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options = FileOptions.None, int bufferSize = FileSystemDefaults.DefaultFileStreamBufferSize)
        {
            var stream = fileSystem.TryOpen(path, fileAccess, fileMode, share, options, bufferSize);
            if (stream == null)
            {
                // Stream is null when the file or a directory is not found.
                if (fileSystem.DirectoryExists(path.Parent!))
                {
                    throw new FileNotFoundException($"The file '{path}' does not exist.");
                }

                throw new DirectoryNotFoundException($"The directory '{path.Parent}' does not exist.");
            }

            return stream.Value;
        }
        
        /// <summary>
        /// Creates a stream to an existing file.
        /// Note that the permissions are such that it is readable, but not
        /// writable by the caller.
        /// </summary>
        public static StreamWithLength? TryOpenReadOnly(this IAbsFileSystem fileSystem, AbsolutePath path, FileShare share)
        {
            return fileSystem.TryOpen(path, FileAccess.Read, FileMode.Open, share);
        }

        /// <summary>
        /// Open the named file asynchronously for reading.
        /// </summary>
        /// <exception cref="FileNotFoundException">Throws if the file is not found.</exception>
        /// <remarks>
        /// Unlike <see cref="TryOpenReadOnly"/> this function throws an exception if the file is missing.
        /// </remarks>
        public static StreamWithLength OpenReadOnly(this IAbsFileSystem fileSystem, AbsolutePath path, FileShare share)
        {
            var stream = fileSystem.TryOpenReadOnly(path, share);
            if (stream == null)
            {
                throw new FileNotFoundException($"The file '{path}' does not exist.");
            }

            return stream.Value;
        }

        /// <summary>
        /// Open the named file asynchronously for reading and sets the expected file length for performance reasons if available.
        /// </summary>
        /// <remarks>
        /// Setting the file length up-front leads to 20-30% performance improvements.
        /// </remarks>
        public static StreamWithLength? TryOpenForWrite(
            this IAbsFileSystem fileSystem,
            AbsolutePath path,
            long? expectingLength,
            FileMode fileMode,
            FileShare share,
            FileOptions options = FileOptions.None,
            int bufferSize = FileSystemDefaults.DefaultFileStreamBufferSize)
        {
            var stream = fileSystem.TryOpen(path, FileAccess.Write, fileMode, share, options, bufferSize);
            if (stream == null)
            {
                return null;
            }

            if (expectingLength != null)
            {
                stream.Value.Stream.SetLength(expectingLength.Value);
            }

            return stream.Value;
        }

        /// <summary>
        /// Open the named file asynchronously for reading and sets the expected file length for performance reasons if available.
        /// </summary>
        /// <exception cref="FileNotFoundException">Throws if the file is not found.</exception>
        /// <exception cref="DirectoryNotFoundException">Throws if the directory is not found.</exception>
        /// <remarks>
        /// Setting the file length up-front leads to 20-30% performance improvements.
        /// </remarks>
        public static StreamWithLength OpenForWrite(
            this IAbsFileSystem fileSystem,
            AbsolutePath path,
            long? expectingLength,
            FileMode fileMode,
            FileShare share,
            FileOptions options = FileOptions.None,
            int bufferSize = FileSystemDefaults.DefaultFileStreamBufferSize)
        {
            var stream = fileSystem.TryOpenForWrite(path, expectingLength, fileMode, share, options, bufferSize);
            if (stream == null)
            {
                // Stream is null when the file or a directory is not found.
                if (fileSystem.DirectoryExists(path.Parent!))
                {
                    throw new FileNotFoundException($"The file '{path}' does not exist.");
                }

                throw new DirectoryNotFoundException($"The directory '{path.Parent}' does not exist.");
            }

            return stream.Value;
        }
    }
}

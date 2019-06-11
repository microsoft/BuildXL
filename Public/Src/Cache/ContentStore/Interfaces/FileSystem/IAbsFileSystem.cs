// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     Super IFileSystem interface with additional methods.
    /// </summary>
    public interface IAbsFileSystem : IFileSystem<AbsolutePath>, IDisposable
    {
        /// <summary>
        ///     Open the named file asynchronously for reading.
        /// </summary>
        /// <param name="path">Path to the existing file that is to be read.</param>
        /// <param name="fileAccess">Read, write, or both</param>
        /// <param name="fileMode">File creation options</param>
        /// <param name="share">Control of other object access to the same file.</param>
        /// <param name="options">Minimum required options.</param>
        /// <param name="bufferSize">Size of the stream's buffer.</param>
        /// <returns>Null if the file or directory does not exist, otherwise the stream.</returns>
        /// <remarks>
        /// Unlike System.IO.FileStream, this provides a way to atomically check for the existence of a file and open it.
        /// This method throws the same set of exceptions that <see cref="FileStream"/> constructor does.
        /// </remarks>
        Task<Stream> OpenAsync(AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options, int bufferSize);

        /// <summary>
        ///     Copy a file from one path to another.
        /// </summary>
        Task CopyFileAsync(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting);

        /// <summary>
        ///     Copy a file from one path to another synchronously.
        /// </summary>
        void CopyFile(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting);

        /// <summary>
        ///     Get the attributes of a file.
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>Attributes of the file</returns>
        FileAttributes GetFileAttributes(AbsolutePath path);

        /// <summary>
        ///     Set the attributes of a file.
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <param name="attributes">Attributes to set</param>
        void SetFileAttributes(AbsolutePath path, FileAttributes attributes);

        /// <summary>
        ///     Checks whether the attributes of the file (including any "unsupported" ones) are a subset of the given attributes
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <param name="attributes">Attributes to check against</param>
        /// <returns>If the file's attributes are a subset</returns>
        bool FileAttributesAreSubset(AbsolutePath path, FileAttributes attributes);

        /// <summary>
        ///     Enumerates directories under the given path
        /// </summary>
        /// <param name="path">Root path under which directories are enumerated.</param>
        /// <param name="options">Whether to recurse or not.</param>
        /// <returns>The directories under the root.</returns>
        IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, EnumerateOptions options);

        /// <summary>
        /// Enumerates files under the given <paramref name="path"/> and calls the <paramref name="fileHandler"/> for every file that matches the given <paramref name="pattern"/>.
        /// </summary>
        /// <exception cref="IOException">Throw if IO error occurs.</exception>
        /// <remarks>
        /// Unlike <see cref="EnumerateDirectories"/> this method uses push-approach (callback-based) instead of using pull-based approach (based on IEnumerable).
        /// This is an example of leaky abstraction because the underlying layer is implemented based on callbacks as well.
        /// </remarks>
        void EnumerateFiles(AbsolutePath path, string pattern, bool recursive, Action<FileInfo> fileHandler);

        /// <summary>
        ///     Creates a hard link pointing to an existing file.
        /// </summary>
        /// <param name="sourceFileName">Path to existing file</param>
        /// <param name="destinationFileName">Path to new file</param>
        /// <param name="replaceExisting">True to overwrite a file at the destination.</param>
        /// <returns>A CreateHardLinkResult enum with the result of the operation</returns>
        CreateHardLinkResult CreateHardLink(AbsolutePath sourceFileName, AbsolutePath destinationFileName, bool replaceExisting);

        /// <summary>
        ///     Gets the number of hard links to the file at the path.
        /// </summary>
        /// <param name="path">Path to file</param>
        /// <returns>Number of hard links to the file</returns>
        /// <remarks>Throws if the file does not exist</remarks>
        int GetHardLinkCount(AbsolutePath path);

        /// <summary>
        ///     Gets the volume unique file id of the file at the path.
        /// </summary>
        /// <param name="path">Path to file</param>
        /// <returns>Id of the file</returns>
        /// <remarks>Throws if the file does not exist</remarks>
        ulong GetFileId(AbsolutePath path);

        /// <summary>
        ///     Gets the size of a file in bytes.
        /// </summary>
        /// <param name="path">Path to the file.</param>
        /// <returns>Size of a file in bytes.</returns>
        long GetFileSize(AbsolutePath path);

        /// <summary>
        ///     Gets the last access time of the file.
        /// </summary>
        /// <param name="path">Path to the file.</param>
        /// <returns>last access time</returns>
        /// <remarks>Is not automatically set depending on NTFS volume settings.</remarks>
        DateTime GetLastAccessTimeUtc(AbsolutePath path);

        /// <summary>
        ///     Sets the last access time of the file.
        /// </summary>
        /// <param name="path">Path to the file.</param>
        /// <param name="lastAccessTimeUtc">Last access time</param>
        /// <remarks>Is not automatically set depending on NTFS volume settings.</remarks>
        void SetLastAccessTimeUtc(AbsolutePath path, DateTime lastAccessTimeUtc);

        /// <summary>
        ///     Add an ACL on the given file which disallows writing or appending data.
        /// </summary>
        /// <param name="path">Path to the file</param>
        void DenyFileWrites(AbsolutePath path);

        /// <summary>
        ///     Add an ACL on the given file which allows writing or appending data.
        /// </summary>
        /// <param name="path">Path to the file</param>
        void AllowFileWrites(AbsolutePath path);

        /// <summary>
        ///     Add an ACL on the given file which disallows writing attributes.
        /// </summary>
        /// <param name="path">Path to the file</param>
        void DenyAttributeWrites(AbsolutePath path);

        /// <summary>
        ///     Add an ACL on the given file which allows writing attributes.
        /// </summary>
        /// <param name="path">Path to the file</param>
        void AllowAttributeWrites(AbsolutePath path);

        /// <summary>
        ///     Get the temporary directory.
        /// </summary>
        AbsolutePath GetTempPath();

        // ReSharper disable once UnusedMember.Global

        /// <summary>
        ///     Flushes all disk buffers associated with the volume.
        /// </summary>
        /// <param name="driveLetter">Drive letter of the volume to flush</param>
        void FlushVolume(char driveLetter);

        /// <summary>
        ///     Get information on the volume hosting the given path.
        /// </summary>
        VolumeInfo GetVolumeInfo(AbsolutePath path);
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage
{
    /// <summary>
    /// Static utilities for manipulating files.
    /// </summary>
    public static class FileUtilities
    {
        private const int DefaultBufferSize = 4096;
        private const int NumberOfDeletionAttempts = 3;

        /// <summary>
        /// Creates all directories up to the given path
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the directory creation fails in a recoverable manner (e.g. access denied).
        /// Note that no exception is thrown if all directories already exist.
        /// </exception>
        /// <remarks>
        /// The current implementation actually blocks. In the future, we expect that this work can be assigned to a pool of I/O
        /// threads
        /// (same with other blocking APIs provided by Windows for I/O). We leave it as async-looking for now so it is 'contagious'
        /// to callers
        /// that are in fact doing I/O.
        /// Does not support paths beyond MAX_PATH
        /// </remarks>
        public static Task<DirectoryInfo> CreateDirectoryAsync(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            return ExceptionUtilities.HandleRecoverableIOException(
                () => Task.FromResult(Directory.CreateDirectory(path)),
                ex => { throw new BuildXLException("Create directory failed", ex); });
        }

        /// <summary>
        /// Creates all directories up to the given path
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the directory creation fails in a recoverable manner (e.g. access denied).
        /// Does not support paths beyond MAX_PATH
        /// </exception>
        public static DirectoryInfo CreateDirectory(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            return ExceptionUtilities.HandleRecoverableIOException(
                () => Directory.CreateDirectory(path),
                ex => { throw new BuildXLException("Create directory failed", ex); });
        }

        /// <summary>
        /// Returns a new <see cref="FileStream" /> with the given creation mode, access level, and sharing.
        /// The returned file stream is opened in 'async' mode, and so its ReadAsync / WriteAsync methods
        /// will dispatch to the managed thread pool's I/O completion port.
        /// </summary>
        /// <remarks>
        /// This factory exists purely because FileStream cannot be constructed in async mode without also specifying a buffer
        /// size.
        /// We go ahead and wrap recoverable errors as BuildXLExceptions as well. See <see cref="CreateFileStream" />.
        ///
        /// Does not support paths longer than MAX_PATH
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if a recoverable error occurs while opening the stream (see
        /// <see cref="ExceptionUtilities.HandleRecoverableIOException{T}" />)
        /// </exception>
        /// <param name="path">Path of file</param>
        /// <param name="fileMode">FileMode</param>
        /// <param name="fileAccess">FileAccess</param>
        /// <param name="fileShare">FileShare</param>
        /// <param name="options">FileOptions</param>
        /// <param name="force">Clears the readonly attribute if necessary</param>
        /// <param name="allowExcludeFileShareDelete">Indicates willful omission of FILE_SHARE_DELETE.</param>
        public static FileStream CreateAsyncFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            Contract.Requires(allowExcludeFileShareDelete || ((fileShare & FileShare.Delete) != 0));
            Contract.EnsuresOnThrow<BuildXLException>(true);

            return CreateFileStream(
                path,
                fileMode,
                fileAccess,
                fileShare,
                options | FileOptions.Asynchronous,
                force: force,
                allowExcludeFileShareDelete: allowExcludeFileShareDelete);
        }

        /// <summary>
        /// Returns a new <see cref="FileStream" /> with the given creation mode, access level, and sharing.
        /// </summary>
        /// <remarks>
        /// This factory exists purely because FileStream cannot be constructed in async mode without also specifying a buffer
        /// size.
        /// We go ahead and wrap recoverable errors as BuildXLExceptions as well.
        ///
        /// Does not support paths longer than MAX_PATH
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if a recoverable error occurs while opening the stream (see
        /// <see cref="ExceptionUtilities.HandleRecoverableIOException{T}" />)
        /// </exception>
        /// <param name="path">Path of file</param>
        /// <param name="fileMode">FileMode</param>
        /// <param name="fileAccess">FileAccess</param>
        /// <param name="fileShare">FileShare</param>
        /// <param name="options">FileOptions</param>
        /// <param name="force">Clears the readonly attribute if necessary</param>
        /// <param name="allowExcludeFileShareDelete">Indicates willful omission of FILE_SHARE_DELETE.</param>
        public static FileStream CreateFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false)
        {
            Contract.Requires(allowExcludeFileShareDelete || ((fileShare & FileShare.Delete) != 0));
            Contract.EnsuresOnThrow<BuildXLException>(true);

            // The bufferSize of 4096 bytes is the default as used by the other FileStream constructors 
            // http://index/mscorlib/system/io/filestream.cs.html
            return ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    try
                    {
                        return new FileStream(path, fileMode, fileAccess, fileShare, bufferSize: DefaultBufferSize, options: options);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // This is a workaround to allow write access to a file that is marked as readonly. It is
                        // exercised when hashing the output files of pips that create readonly files. The hashing currently
                        // opens files as write
                        if (force)
                        {
                            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                            return new FileStream(path, fileMode, fileAccess, fileShare, bufferSize: DefaultBufferSize, options: options);
                        }

                        throw;
                    }
                },
                ex => { throw new BuildXLException(string.Format(CultureInfo.InvariantCulture, "Failed to open path '{0}' with mode='{fileMode}', access='{fileAccess}', share='{fileShare}'", path), ex); });
        }
    }
}

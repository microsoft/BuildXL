// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Storage
{
    /// <summary>
    /// Disposable container for directory handles, preventing deletion of those directories.
    /// This allows establishment of directory-existence to remain invariant until disposed.
    /// </summary>
    /// <remarks>
    /// This class is not thread safe.
    /// Holding directories un-delete-able like this requires a handle per directory.
    /// Consequently, only attempt to lock a bounded number of directories.
    /// </remarks>
    public sealed class DirectoryDeletionLock : IDisposable
    {
        private readonly List<SafeFileHandle> m_directoryHandles = new List<SafeFileHandle>();

        /// <summary>
        /// Ensures that a directory exists, and then holds it to prevent deletion.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if an I/O error prevents creating or holding the directory.</exception>
        public void CreateAndPreventDeletion(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            if (!Helpers.RetryOnFailure(final => CreateAndPreventDeletionHelper(path)))
            {
                throw new BuildXLException(I($"Failed to lock the directory '{path}' (to prevent deletion) after creating it"));
            }
        }

        /// <summary>
        /// Ensures that a redirected directory redirection exists, and then holds it to prevent deletion.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if an I/O error prevents creating or holding the redirected directory.</exception>
        public void CreateRedirectionAndPreventDeletion(string redirectedPath, string realPath, bool deleteExisting, bool deleteOnClose)
        {
            Contract.Requires(!string.IsNullOrEmpty(redirectedPath));
            Contract.Requires(!string.IsNullOrEmpty(realPath));

            if (!Helpers.RetryOnFailure(final => CreateRedirectionAndPreventDeletionHelper(redirectedPath, realPath, deleteExisting, deleteOnClose)))
            {
                throw new BuildXLException(I($"Failed to lock the redirected directory '{redirectedPath} -> {realPath}' (to prevent deletion) after creating it"));
            }
        }

        private bool CreateRedirectionAndPreventDeletionHelper(string redirectedPath, string realPath, bool deleteExisting, bool deleteOnClose)
        {
            Contract.Requires(!string.IsNullOrEmpty(redirectedPath));
            Contract.Requires(!string.IsNullOrEmpty(realPath));

            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    var probeRedirectedPath = FileUtilities.TryProbePathExistence(redirectedPath, false);
                    if (!probeRedirectedPath.Succeeded)
                    {
                        throw probeRedirectedPath.Failure.Throw();
                    }

                    if (deleteExisting)
                    {
                        if (probeRedirectedPath.Result == PathExistence.ExistsAsFile)
                        {
                            if (!OperatingSystemHelper.IsUnixOS)
                            {
                                if (!FileUtilities.TryRemoveDirectory(redirectedPath, out var hr))
                                {
                                    throw new NativeWin32Exception(hr);
                                }
                            }
                            else
                            {
                                FileUtilities.DeleteFile(redirectedPath, waitUntilDeletionFinished: true);
                            }
                        }
                        else if (probeRedirectedPath.Result == PathExistence.ExistsAsDirectory)
                        {
                            FileUtilities.DeleteDirectoryContents(redirectedPath, deleteRootDirectory: true);
                        }
                    }

                    Possible<Unit> maybeCreated = Unit.Void;

                    if (!OperatingSystemHelper.IsUnixOS)
                    {
                        // On some Windows OS, creating directory symlinks is not allowed, so we create a junction.
                        Directory.CreateDirectory(redirectedPath);
                        FileUtilities.CreateJunction(redirectedPath, realPath);
                    }
                    else
                    {
                        maybeCreated = FileUtilities.TryCreateSymbolicLink(redirectedPath, realPath, isTargetFile: false);
                    }

                    if (!maybeCreated.Succeeded)
                    {
                        throw maybeCreated.Failure.Throw();
                    }
                },
                ex => { throw new BuildXLException(I($"Failed to create the redirected directory '{redirectedPath}'"), ex); });

            return LockDirectory(redirectedPath, deleteOnClose);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Function will eventually behave like the original.")]
        private bool CreateAndPreventDeletionHelper(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    Directory.CreateDirectory(path);
                },
                ex => { throw new BuildXLException(I($"Failed to create the directory '{path}'"), ex); });

            return LockDirectory(path, false);
        }

        private bool LockDirectory(string path, bool deleteOnClose)
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                OpenFileResult result = FileUtilities.TryOpenDirectory(
                    path,
                    FileDesiredAccess.GenericRead,
                    FileShare.ReadWrite,
                    FileFlagsAndAttributes.FileFlagOpenReparsePoint 
                    | (deleteOnClose ? FileFlagsAndAttributes.FileFlagDeleteOnClose : FileFlagsAndAttributes.None),
                    out SafeFileHandle handle);

                if (result.Succeeded)
                {
                    // TODO: Should verify we opened a directory (FILE_ATTRIBUTE_DIRECTORY)
                    Contract.Assert(handle != null);
                    m_directoryHandles.Add(handle);

                    return true;
                }

                Contract.Assert(handle == null);
                switch (result.Status)
                {
                    case OpenFileStatus.FileAlreadyExists:
                        Contract.Assume(false, "We did not require creation of a new path (we required an existing path)");
                        throw new InvalidOperationException();
                    case OpenFileStatus.Success:
                        throw Contract.AssertFailure("Result was successful");
                    case OpenFileStatus.UnknownError:
                        throw result.ThrowForUnknownError();
                    default:
                        throw result.ThrowForKnownError();
                }
            }
            else
            {
                // Just create the directory in the .NET Core case for now and do not keep a deletion lock
                return true;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (SafeFileHandle handle in m_directoryHandles)
            {
                handle.Dispose();
            }

            m_directoryHandles.Clear();
        }
    }
}

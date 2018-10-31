// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Native.IO;
using BuildXL.Native.Streams;
using BuildXL.Native.Streams.Unix;
using BuildXL.Native.Streams.Windows;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage
{
    /// <summary>
    /// A factory to create AsyncFiles with different configurations
    /// </summary>
    public static class AsyncFileFactory
    {
        /// <summary>
        /// Creates a concrete <see cref="IAsyncFile"/> instance. Throws on failure.
        /// </summary>
        /// <remarks>
        /// TODO: Currently returns a platform specific concrete instance of <see cref="IAsyncFile"/>
        /// </remarks>
        public static IAsyncFile CreateAsyncFile(
            SafeFileHandle handle,
            FileDesiredAccess access,
            bool ownsHandle,
            IIOCompletionManager ioCompletionManager = null,
            string path = null,
            FileKind kind = FileKind.File)
        {
            return OperatingSystemHelper.IsUnixOS
            ? (IAsyncFile) new AsyncFileUnix(handle, access, ownsHandle, path, kind)
            : (IAsyncFile) new AsyncFileWin(handle, access, ownsHandle, ioCompletionManager, path, kind);
        }

        /// <summary>
        /// Creates or opens a <see cref="IAsyncFile"/>. Throws on failure.
        /// </summary>
        /// <exception cref="BuildXL.Native.IO.NativeWin32Exception">Thrown on failure</exception>
        public static IAsyncFile CreateOrOpen(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition,
            FileFlagsAndAttributes flagsAndAttributes,
            IIOCompletionManager ioCompletionManager = null)
        {
            IAsyncFile file;
            OpenFileResult result = TryCreateOrOpen(
                path,
                desiredAccess,
                shareMode,
                creationDisposition,
                flagsAndAttributes | FileFlagsAndAttributes.FileFlagOverlapped,
                out file,
                ioCompletionManager);

            if (file != null)
            {
                Contract.Assert(result.Succeeded);
                return file;
            }

            Contract.Assert(!result.Succeeded);
            throw result.ThrowForError();
        }

        /// <summary>
        /// Returns an <see cref="IAsyncFile"/>. Does not throw on failure.
        /// </summary>
        private static OpenFileResult TryCreateOrOpen(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition,
            FileFlagsAndAttributes flagsAndAttributes,
            out IAsyncFile openedFile,
            IIOCompletionManager ioCompletionManager = null)
        {
            SafeFileHandle handle;
            OpenFileResult result = FileUtilities.TryCreateOrOpenFile(
                path,
                desiredAccess,
                shareMode,
                creationDisposition,
                flagsAndAttributes | FileFlagsAndAttributes.FileFlagOverlapped,
                out handle);

            if (handle != null)
            {
                Contract.Assert(result.Succeeded && !handle.IsInvalid);
                openedFile = CreateAsyncFile(handle, desiredAccess, ownsHandle: true, ioCompletionManager: ioCompletionManager, path: path);
            }
            else
            {
                Contract.Assert(!result.Succeeded);
                openedFile = null;
            }

            return result;
        }
    }
}

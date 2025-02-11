// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

#nullable enable

// This file is shared between BuildXL and VBCSCompilerLogger. It is used to report file accesses to the sandbox.
// Both projects have different namespaces, so we need to use a conditional compilation symbol to define the namespace.
// The reason for this source sharing is to prevent VBCSCompilerLogger from depending on BuildXL dlls, in particular BuildXL.Processes.dll.
// The primary consumer of VBCSCompiler logger is MSBuild. MSBuild relies on BuildXL dlls, in particular MSBuild comes with BuildXL.Processes.dll
// for its sandboxing, i.e., using BuildXL Detours sandbox. However, the versions of those dlls can be old or different from the ones
// VBCSCompiler logger depend on. An example of this mismatched version is when VBCSCompiler logger is "injected" by QuickBuild.
// When consuming this logger, MSBuild will load the BuildXL.Processes.dll that comes with its deployment, and not what this logger depends on.
// This can cause a runtime issue.
#if VBCS_COMPILER_LOGGER
namespace VBCSCompilerLogger
#else
namespace BuildXL.Processes
#endif
{
    /// <summary>
    /// This class is the entry point for trusted tools to report file accesses directly to the sandbox, without
    /// actually performing any IO
    /// </summary>
    /// <remarks>
    /// The main use case for this reporter is when pips are configured to allow trusted processes to breakaway from the sandbox. In that circumstance,
    /// processes may need to compensate for some unobserved accesses.
    /// In order for processes to use this reporter, they must run in the context of a pip which has a non-empty set of processes that 
    /// are allowed to breakaway. Otherwise, attempting to report an access will fail.
    /// Observe this reporter is only intended for internal (to BuildXL) tools at this point, since it requires tools to link against
    /// this particular BuildXL library. In the future we may consider a more general approach for arbitrary (trusted) tools to perform these actions
    /// </remarks>
    public sealed class AugmentedManifestReporter
    {
        /// <summary>
        /// We shouldn't try to close this handle. Detours takes care of that.
        /// </summary>
        private readonly SafeFileHandle m_detoursReportHandle;
        private readonly UnicodeEncoding m_encoding;

        /// <nodoc/>
        public static readonly AugmentedManifestReporter Instance = CreateFromBuildXLEnvVar();

        private AugmentedManifestReporter(SafeFileHandle detoursReportHandle, UnicodeEncoding? encoding)
        {
            m_encoding = encoding ?? new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
            m_detoursReportHandle = detoursReportHandle;
        }

        /// <summary>
        /// Creates an instance of the reporter with the given handle.
        /// </summary>
        public static AugmentedManifestReporter Create(SafeFileHandle detoursReportHandle) => new(detoursReportHandle, encoding: null);

        /// <summary>
        /// Creates an instance of the reporter with the given handle and encoding.
        /// </summary>
        public static AugmentedManifestReporter Create(SafeFileHandle detoursReportHandle, UnicodeEncoding encoding) => new(detoursReportHandle, encoding);

        /// <summary>
        /// Creates an instance of the reporter from the BUILDXL_AUGMENTED_MANIFEST_HANDLE environment variable.
        /// </summary>
        public static AugmentedManifestReporter CreateFromBuildXLEnvVar()
        {
            var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

            // CODESYNC: Keep variable name in sync with DetoursServices on the C++ side
            string handleAsString = Environment.GetEnvironmentVariable("BUILDXL_AUGMENTED_MANIFEST_HANDLE") ?? string.Empty;

            // If the expected environment variable with the handle pointer is not set, just return. We'll check this
            // when reporting an access
            SafeFileHandle detoursReportHandle = string.IsNullOrEmpty(handleAsString)
                || !int.TryParse(handleAsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int handlePtr)
                ? new SafeFileHandle(new IntPtr(-1), true) // Invalid handle
                : new SafeFileHandle(new IntPtr(handlePtr), ownsHandle: false);

            return new AugmentedManifestReporter(detoursReportHandle, encoding);
        }

        /// <summary>
        /// Convenience method to report a collection of file creations
        /// </summary>
        public bool TryReportFileCreations(IEnumerable<string> paths)
        {
            if (m_detoursReportHandle.IsInvalid)
            {
                return false;
            }

            foreach(string path in paths)
            {
                DoReportAccess(
                    ReportedFileOperation.CreateFile,
                    RequestedAccess.Write,
                    FileAccessStatus.Allowed,
                    0,
                    Usn.Zero,
                    DesiredAccess.GENERIC_WRITE,
                    ShareMode.FILE_SHARE_NONE,
                    CreationDisposition.CREATE_ALWAYS,
                    FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                    FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                    path);
            }

            return true;
        }

        /// <summary>
        /// Convenience method to report a collection of file reads
        /// </summary>
        public bool TryReportFileReads(IEnumerable<string> paths)
        {
            if (m_detoursReportHandle.IsInvalid)
            {
                return false;
            }

            foreach (string path in paths)
            {
                DoReportAccess(
                    ReportedFileOperation.CreateFile,
                    RequestedAccess.Read,
                    FileAccessStatus.Allowed,
                    0,
                    Usn.Zero,
                    DesiredAccess.GENERIC_READ,
                    ShareMode.FILE_SHARE_NONE,
                    CreationDisposition.OPEN_ALWAYS,
                    FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                    FlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
                    path);
            }

            return true;
        }

        private void DoReportAccess(
            ReportedFileOperation reportedFileOperation, 
            RequestedAccess requestedAccess, 
            FileAccessStatus fileAccessStatus, 
            uint errorCode, 
            Usn usn, 
            DesiredAccess desiredAccess, 
            ShareMode shareMode, 
            CreationDisposition creationDisposition, 
            FlagsAndAttributes flagsAndAttributes,
            FlagsAndAttributes openedFileOrDirectoryAttributes,
            string path)
        {
            var process = Process.GetCurrentProcess();

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Provided path is null or empty", nameof(path));
            }

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException($"Provided path is expected to be rooted, but found '{path}'.", nameof(path));
            }

            // The given path may not be canonicalized (e.g. it may contain '..')
            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                // If getting the full path fails, we follow a behavior similar to what detours does, where the access is ignored
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                return;
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
            }

            string access = GetReportLineForAugmentedFileAccess(
                reportedFileOperation,
                (uint)process.Id,
                requestedAccess,
                fileAccessStatus,
                errorCode,
                usn,
                desiredAccess,
                shareMode,
                creationDisposition,
                flagsAndAttributes,
                openedFileOrDirectoryAttributes,
                fullPath);

            if (!TryWriteFileSync(m_detoursReportHandle, m_encoding.GetBytes(access), out int nativeErrorCode))
            {
                // Something didn't go as expected. We cannot let the process continue if we failed at reporting an access
                throw new Win32Exception(nativeErrorCode, $"Writing augmented file access report failed. Line: {access}");
            }
        }

        private static string GetReportLineForAugmentedFileAccess(
            ReportedFileOperation reportedFileOperation,
            uint processId,
            RequestedAccess requestedAccess,
            FileAccessStatus fileAccessStatus,
            uint errorCode,
            Usn usn,
            DesiredAccess desiredAccess,
            ShareMode shareMode,
            CreationDisposition creationDisposition,
            FlagsAndAttributes flagsAndAttributes,
            FlagsAndAttributes openedFileOrDirectoryAttributes,
            string absolutePath) =>
            $"{(int)ReportType.AugmentedFileAccess},{reportedFileOperation}:{processId:x}|{0:x}|{0:x}|{(byte)requestedAccess:x}|{(byte)fileAccessStatus:x}|1|{errorCode:x}|{errorCode:x}|{usn.Value:x}|{(uint)desiredAccess:x}|{(uint)shareMode:x}|{(uint)creationDisposition:x}|{(uint)flagsAndAttributes:x}|{(uint)openedFileOrDirectoryAttributes:x}|{0:x}|{absolutePath}\r\n";

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern unsafe bool WriteFile(
            SafeFileHandle handle,
            byte[] buffer,
            int numBytesToWrite,
            out int numBytesWritten,
            NativeOverlapped* lpOverlapped);

        private static bool TryWriteFileSync(SafeFileHandle handle, byte[] content, out int nativeErrorCode)
        {
            bool result;

            unsafe
            {
                // TODO: Instead of using unsafe code, we could use FileStream, but this needs further investigation
                //       because FileStream assumes that it has exclusive control over the handle.
                result = WriteFile(handle, content, content.Length, out _, lpOverlapped: null);
            }

            nativeErrorCode = Marshal.GetLastWin32Error();

            return result;
        }

        #region Native structures

        // Native structures and enums to support Augmented manifest reporter.
        // The structures and enums need to be codesynced with BuildXL.Native and BuildXL.Processes.
        // The duplication is necessary to avoid a dependency on BuildXL.Native and BuildXL.Processes because this class
        // is shared between BuildXL and VBCSCompilerLogger; see the comment at the top of this file.

        /// <summary>
        /// CODESYNC: BuildXL.Native.IO.Usn
        /// </summary>
        private readonly struct Usn
        {
            public static readonly Usn Zero = new(0);

            public ulong Value { get; init; }

            private Usn(ulong value) => Value = value;
        }

        /// <summary>
        /// CODESYNC: BuildXL.Processes.ReportedFileOperation
        /// </summary>
        private enum ReportedFileOperation : byte
        {
            CreateFile = 1,
        }

        /// <summary>
        /// CODESYNC: BuildXL.Processes.RequestedAccess
        /// </summary>
        [Flags]
        private enum RequestedAccess : byte
        {
            None = 0,
            Read = 1,
            Write = 2
        }

        /// <summary>
        /// CODESYNC: BuildXL.Processes.FileAccessStatus
        /// </summary>
        [Flags]
        private enum FileAccessStatus : byte
        {
            None = 0,
            Allowed = 1
        }

        /// <summary>
        /// CODESYNC: BuildXL.Processes.DesiredAccess
        /// </summary>
        [Flags]
        private enum DesiredAccess : uint
        {
            GENERIC_WRITE = 0x40000000,
            GENERIC_READ = 0x80000000
        }

        /// <summary>
        /// CODESYNC: BuildXL.Processes.ShareMode
        /// </summary>
        [Flags]
        private enum ShareMode : uint
        {
            FILE_SHARE_NONE = 0x0
        }

        /// <summary>
        /// CODESYNC: BuildXL.Processes.CreationDisposition
        /// </summary>
        private enum CreationDisposition : uint
        {
            CREATE_ALWAYS = 2,
            OPEN_ALWAYS = 4,
        }

        /// <summary>
        /// CODESYNC: BuildXL.Processes.FlagsAndAttributes
        /// </summary>
        [Flags]
        private enum FlagsAndAttributes : uint
        {
            FILE_ATTRIBUTE_NORMAL = 0x00000080
        }

        /// <summary>
        /// CODESYNC: BuildXL.Processes.ReportType
        /// </summary>
        private enum ReportType
        {
            AugmentedFileAccess = 6,
        }

        #endregion
    }
}

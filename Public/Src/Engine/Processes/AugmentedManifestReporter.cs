// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Text;
using BuildXL.Native.IO;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Processes
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
        [CanBeNull]
        private readonly SafeFileHandle m_detoursReportHandle = null;
        private readonly UnicodeEncoding m_encoding;

        /// <nodoc/>
        public static AugmentedManifestReporter Instance = new AugmentedManifestReporter();

        private AugmentedManifestReporter()
        {
            m_encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

            // Keep variable name in sync with DetoursServices on the C++ side
            string handleAsString = Environment.GetEnvironmentVariable("BUILDXL_AUGMENTED_MANIFEST_HANDLE");
            
            // If the expected environment variable with the handle pointer is not set, just return. We'll check this
            // when reporting an access
            if (string.IsNullOrEmpty(handleAsString))
            {
                return;
            }

            var success = int.TryParse(handleAsString, NumberStyles.Integer, CultureInfo.InvariantCulture, out int handlePtr);
            Contract.Assert(success);

            // We shouldn't try to close this handle. Detours takes care of that.
            m_detoursReportHandle = new SafeFileHandle(new IntPtr(handlePtr), ownsHandle: false);
        }

        /// <summary>
        /// Attempts to report a file access to BuildXL without actually performing any IO
        /// </summary>
        /// <remarks>
        /// Failure means this method was invoked from a process that was not configured to breakaway from the sandbox
        /// </remarks>
        public bool TryReportAugmentedFileAccess(
            ReportedFileOperation reportedFileOperation,
            RequestedAccess requestedAccess,
            FileAccessStatus fileAccessStatus,
            uint errorCode,
            Usn usn,
            DesiredAccess desiredAccess,
            ShareMode shareMode,
            CreationDisposition creationDisposition,
            FlagsAndAttributes flagsAndAttributes,
            string path,
            [CanBeNull]string enumeratePattern = null,
            [CanBeNull]string processArgs = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            if (m_detoursReportHandle == null)
            {
                return false;
            }

            DoReportAccess(
                reportedFileOperation,
                requestedAccess,
                fileAccessStatus,
                errorCode,
                usn,
                desiredAccess,
                shareMode,
                creationDisposition,
                flagsAndAttributes,
                path,
                enumeratePattern,
                processArgs);

            return true;
        }

        /// <summary>
        /// Convenience method to report a collection of file creations
        /// </summary>
        public bool TryReportFileCreations(IEnumerable<string> paths)
        {
            if (m_detoursReportHandle == null)
            {
                return false;
            }

            foreach(string path in paths)
            {
                Contract.Requires(!string.IsNullOrEmpty(path));

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
                    path,
                    enumeratePattern: null,
                    processArgs: null);
            }

            return true;
        }

        /// <summary>
        /// Convenience method to report a collection of file reads
        /// </summary>
        public bool TryReportFileReads(IEnumerable<string> paths)
        {
            if (m_detoursReportHandle == null)
            {
                return false;
            }

            foreach (string path in paths)
            {
                Contract.Requires(!string.IsNullOrEmpty(path));

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
                    path,
                    enumeratePattern: null,
                    processArgs: null);
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
            string path, 
            string enumeratePattern, 
            string processArgs)
        {
            var process = Process.GetCurrentProcess();

            string access = FileAccessReportLine.GetReportLineForAugmentedFileAccess(
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
                path,
                enumeratePattern,
                processArgs);

            if (!FileUtilities.TryWriteFileSync(m_detoursReportHandle, m_encoding.GetBytes(access), out int nativeErrorCode))
            {
                // Something didn't go as expected. We cannot let the process continue if we failed at reporting an access
                throw new NativeWin32Exception(nativeErrorCode, $"Writing augmented file access report failed. Line: {access}");
            }
        }
    }
}

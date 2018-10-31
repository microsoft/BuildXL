// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591

namespace BuildXL.Native.Tracing
{
    /// <summary>
    /// Logging for Native
    /// </summary>
    public class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log { get; } = new Logger();

        public void FileUtilitiesDiagnostic(LoggingContext context, string path, string description)
        {
            context.SpecifyVerboseWasLogged((int)EventId.FileUtilitiesDiagnostic);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine("FileUtilities: '{0}'. {1}", path, description);
            }
        }

        public void RetryOnFailureException(LoggingContext context, string exception)
        {
            context.SpecifyVerboseWasLogged((int)EventId.RetryOnFailureException);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine("Retry attempt failed with exception. {0}", exception);
            }
        }

        public void SettingOwnershipAndAcl(LoggingContext context, string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.SettingOwnershipAndAcl);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine("Attempting to set ownership and ACL to path '{0}'.", path);
            }
        }

        public void SettingOwnershipAndAclFailed(LoggingContext context, string path, string filename, string arguments, string reason)
        {
            context.SpecifyVerboseWasLogged((int)EventId.SettingOwnershipAndAclFailed);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine("Failed to set ownership and ACL to path '{0}'. Command {1} {2} {3}", path, filename, arguments, reason);
            }
        }

        public void StorageReadUsn(LoggingContext context, ulong idHigh, ulong idLow, ulong usn)
        {
            context.SpecifyVerboseWasLogged((int)EventId.StorageReadUsn);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine("Read USN: (id {0:X16}-{1:X16}) @ {2:X16}", idHigh, idLow, usn);
            }
        }

        public void StorageCheckpointUsn(LoggingContext context, ulong newUsn)
        {
            context.SpecifyVerboseWasLogged((int)EventId.StorageCheckpointUsn);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine("Checkpoint (new USN): {0:X16}", newUsn);
            }
        }

        public void StorageTryOpenOrCreateFileFailure(LoggingContext context, string path, int creationDisposition, int hresult)
        {
            context.SpecifyVerboseWasLogged((int)EventId.StorageTryOpenOrCreateFileFailure);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(
                    "Creating a file handle for path {0} (disposition 0x{1:X8}) failed with HRESULT 0x{2:X8}",
                    path,
                    creationDisposition,
                    hresult);
            }
        }

        public void StorageTryOpenDirectoryFailure(LoggingContext context, string path, int hresult)
        {
            context.SpecifyVerboseWasLogged((int)EventId.StorageTryOpenDirectoryFailure);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine("Opening a directory handle for path {0} failed with HRESULT 0x{1:X8}", path, hresult);
            }
        }

        public void StorageFoundVolume(LoggingContext context, string volumeGuidPath, ulong serial)
        {
            context.SpecifyVerboseWasLogged((int)EventId.StorageFoundVolume);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine("Found volume {0} (serial: {1:X16})", volumeGuidPath, serial);
            }
        }
        
        public void StorageTryOpenFileByIdFailure(LoggingContext context, ulong idHigh, ulong idLow, ulong volumeSerial, int hresult)
        {
            context.SpecifyVerboseWasLogged((int)EventId.StorageTryOpenFileByIdFailure);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(
                    "Opening the file with file ID {0:X16}-{1:X16} on {2:X16} failed with HRESULT 0x{3:X8}",
                    idHigh,
                    idLow,
                    volumeSerial,
                    hresult);
            }
        }
    }
}

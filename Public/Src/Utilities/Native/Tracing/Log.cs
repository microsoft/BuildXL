// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#endif
#if !FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using System.Diagnostics.Tracing;
#endif

#pragma warning disable 1591

namespace BuildXL.Native.Tracing
{
    /// <summary>
    /// Logging for bxl.exe.
    /// </summary>
    [EventKeywordsType(typeof(Events.Keywords))]
    [EventTasksType(typeof(Events.Tasks))]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log { get { return m_log; } }

        [GeneratedEvent(
            (int)EventId.FileUtilitiesDirectoryDeleteFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Storage,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "FileUtilities: Directory delete for '{path}' failed. An error will be thrown.")]
        public abstract void FileUtilitiesDirectoryDeleteFailed(LoggingContext context, string path);

        [GeneratedEvent(
            (int)EventId.FileUtilitiesDiagnostic,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Storage,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "FileUtilities: '{path}'. {description}")]
        public abstract void FileUtilitiesDiagnostic(LoggingContext context, string path, string description);

        [GeneratedEvent(
            (int)EventId.RetryOnFailureException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Storage,
            Keywords = (int)Events.Keywords.UserMessage,

            // TODO: demote this to a diagnostics level once we sort out our file deletion woes as well as materialization failure (FailIfExist)
            // Keywords = (int)((Events.Keywords.UserMessage) | Events.Keywords.Diagnostics),
            Message = "Retry attempt failed with exception. {exception}")]
        public abstract void RetryOnFailureException(LoggingContext context, string exception);

        [GeneratedEvent(
            (int)EventId.SettingOwnershipAndAcl,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Storage,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Attempting to set ownership and ACL to path '{path}'.")]
        public abstract void SettingOwnershipAndAcl(LoggingContext context, string path);

        [GeneratedEvent(
            (int)EventId.SettingOwnershipAndAclFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Events.Tasks.Storage,
            Keywords = (int)Events.Keywords.UserMessage,
            Message = "Failed to set ownership and ACL to path '{path}'. Command {filename} {arguments} {reason}")]
        public abstract void SettingOwnershipAndAclFailed(LoggingContext context, string path, string filename, string arguments, string reason);

        [GeneratedEvent(
            (int)EventId.StorageReadUsn,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Read USN: (id {0:X16}-{1:X16}) @ {2:X16}")]
        public abstract void StorageReadUsn(LoggingContext context, ulong idHigh, ulong idLow, ulong usn);

        [GeneratedEvent(
            (int)EventId.StorageCheckpointUsn,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Checkpoint (new USN): {0:X16}")]
        public abstract void StorageCheckpointUsn(LoggingContext context, ulong newUsn);

        [GeneratedEvent(
            (int)EventId.StorageTryOpenOrCreateFileFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Creating a file handle for path {0} (disposition 0x{1:X8}) failed with HRESULT 0x{2:X8}")]
        public abstract void StorageTryOpenOrCreateFileFailure(LoggingContext context, string path, int creationDisposition, int hresult);

        [GeneratedEvent(
            (int)EventId.StorageTryOpenDirectoryFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Opening a directory handle for path {0} failed with HRESULT 0x{1:X8}")]
        public abstract void StorageTryOpenDirectoryFailure(LoggingContext context, string path, int hresult);

        [GeneratedEvent(
            (int)EventId.StorageFoundVolume,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Found volume {0} (serial: {1:X16})")]
        public abstract void StorageFoundVolume(LoggingContext context, string volumeGuidPath, ulong serial);
        
        [GeneratedEvent(
            (int)EventId.StorageTryOpenFileByIdFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Opening the file with file ID {0:X16}-{1:X16} on {2:X16} failed with HRESULT 0x{3:X8}")]
        public abstract void StorageTryOpenFileByIdFailure(LoggingContext context, ulong idHigh, ulong idLow, ulong volumeSerial, int hresult);
    }
}

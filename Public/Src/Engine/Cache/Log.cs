// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591

namespace BuildXL.Engine.Cache.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Events.Keywords))]
    [EventTasksType(typeof(Events.Tasks))]
    internal abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        /*
        [GeneratedEvent(
            (int)EventId.StorageCacheCopyLocalError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "While bringing {0} local, the cache reported error: {1}")]
        public abstract void StorageCacheCopyLocalError(LoggingContext context, string contentHash, string errorMessage);
        */
        [GeneratedEvent(
            (int)EventId.StorageFailureToOpenFileForFlushOnIngress,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "The path '{0}' could not be opened to be flushed (in preparation for cache-ingress). This file may subsequently be treated as out-of-date. Open failure: {1}")]
        public abstract void StorageFailureToOpenFileForFlushOnIngress(LoggingContext context, string path, string errorMessage);

        [GeneratedEvent(
            (ushort)EventId.StoreSymlinkWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Storing symlink '{file}' to cache makes builds behave unexpectedly, e.g., cache replays symlinks as concrete files, pip may not rebuild if symlink target is modified, pip may fail if symlink target is nonexistent, etc.")]
        internal abstract void StoreSymlinkWarning(LoggingContext loggingContext, string file);

        [GeneratedEvent(
            (int)EventId.StorageFailureToFlushFileOnDisk,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "The path '{0}' could not be flushed (in preparation for cache-ingress). NtStatus code: {1}")]
        public abstract void StorageFailureToFlushFileOnDisk(LoggingContext context, string path, string errorCode);

        [GeneratedEvent(
            (int)EventId.ClosingFileStreamAfterHashingFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "Closing the stream to the path '{0}' thrown a native ERROR_INCORRECT_FUNCTION exception. exception message: {1}. NtStatus code: {2}.")]
        public abstract void ClosingFileStreamAfterHashingFailed(LoggingContext context, string path, string message, string errorCode);

        [GeneratedEvent(
            (int)EventId.FailedOpenHandleToGetKnownHashDuringMaterialization,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "The path '{0}' could not be opened to get known hash during materialization: {1}")]
        public abstract void FailedOpenHandleToGetKnownHashDuringMaterialization(LoggingContext context, string path, string message);

        [GeneratedEvent(
            (int)EventId.TimeoutOpeningFileForHashing,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "The path '{0}' could not be opened for hashing because the filesystem returned ERROR_TIMEOUT.")]
        public abstract void TimeoutOpeningFileForHashing(LoggingContext context, string path);

        [GeneratedEvent(
            (int)EventId.HashedSymlinkAsTargetPath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.Storage,
            Message = "The path '{0}' was a symbolic link, and it will be hashed based on its target's path, i.e., '{1}', rather than the target's content.")]
        public abstract void HashedSymlinkAsTargetPath(LoggingContext context, string path, string targetPath);

        [GeneratedEvent(
            (int)EventId.TemporalCacheEntryTrace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Storage,
            Message = "{message}")]
        public abstract void TemporalCacheEntryTrace(LoggingContext context, string message);

        [GeneratedEvent(
            (ushort)EventId.SerializingToPipFingerprintEntryResultInCorruptedData,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Serializing to pip fingerprint entry results in corrupted data: Kind: {kind} | Data blob: {blob}")]
        internal abstract void SerializingToPipFingerprintEntryResultInCorruptedData(LoggingContext loggingContext, string kind, string blob);

        [GeneratedEvent(
            (ushort)EventId.DeserializingCorruptedPipFingerprintEntry,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Deserializing corrupted pip fingerprint entry: \r\n\t Kind: {kind}\r\n\t Weak fingerprint: {weakFingerprint}\r\n\t Path set hash: {pathSetHash}\r\n\t Strong fingerprint: {strongFingerprint}\r\n\t Expected pip fingerprint entry hash: {expectedHash}\r\n\t Re-computed pip fingerprint entry hash: {hash}\r\n\t Data blob: {blob}\r\n\t Actual pip fingerprint entry hash: {actualHash}\r\n\t Actual pip fingerprint entry blob: {actualEntryBlob}")]
        internal abstract void DeserializingCorruptedPipFingerprintEntry(LoggingContext loggingContext, string kind, string weakFingerprint, string pathSetHash, string strongFingerprint, string expectedHash, string hash, string blob, string actualHash, string actualEntryBlob);

        [GeneratedEvent(
            (ushort)EventId.RetryOnLoadingAndDeserializingMetadata,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Retry on loading and deserializing metadata: Succeeded: {succeeded} | Retry count: {retryCount}")]
        internal abstract void RetryOnLoadingAndDeserializingMetadata(LoggingContext loggingContext, bool succeeded, int retryCount);
    }
}

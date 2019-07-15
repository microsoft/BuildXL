// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Engine.Cache.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        StorageFailureToOpenFileForFlushOnIngress = 729,

        FailedOpenHandleToGetKnownHashDuringMaterialization = 732,
        HashedSymlinkAsTargetPath = 733,
        
        ClosingFileStreamAfterHashingFailed = 735,
        StorageFailureToFlushFileOnDisk = 736,
        
        StoreSymlinkWarning = 740,
        SerializingToPipFingerprintEntryResultInCorruptedData = 742,
        DeserializingCorruptedPipFingerprintEntry = 743,

        RetryOnLoadingAndDeserializingMetadata = 746,

        TimeoutOpeningFileForHashing = 748,

        TemporalCacheEntryTrace = 2733,
    }
}

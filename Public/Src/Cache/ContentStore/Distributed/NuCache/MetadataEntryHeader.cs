// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.Serialization;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{

    /// <summary>
    /// Header info for metadata entry in memoization database.
    /// </summary>
    [ProtoContract]
    public struct MetadataEntryHeader
    {
        /// <summary>
        /// The replacement token used when updating the entry
        /// </summary>
        [ProtoMember(1)]
        public string ReplacementToken { get; set; }

        /// <summary>
        /// Last update time
        /// </summary>
        [ProtoMember(2)]
        public DateTime LastAccessTimeUtc { get; set; }

        /// <summary>
        /// Update sequence number 
        /// </summary>
        [ProtoMember(3)]
        public long SequenceNumber { get; set; }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.Serialization;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

#nullable enable
namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <summary>
    ///     Information stored about a cache session that can be later restored. To be used in conjunction to <see cref="ContentStore.Service.HibernatedContentSessionInfo"/>.
    ///     These two classes are separated for back compat, and so that in the case that the cache service is not enabled, the content session will
    ///     still be able to parse the session info.
    /// </summary>
    [DataContract]
    public class HibernatedCacheSessionInfo
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="HibernatedCacheSessionInfo"/> class.
        /// </summary>
        public HibernatedCacheSessionInfo(
            int id,
            string? serializedSessionConfig,
            string? pat,
            IList<PublishingOperation>? pendingPublishingOperations)
        {
            Id = id;
            SerializedSessionConfiguration = serializedSessionConfig;
            Pat = pat;
            PendingPublishingOperations = pendingPublishingOperations;
        }

        /// <summary>
        /// Used by Json deserializer.
        /// </summary>
        public HibernatedCacheSessionInfo()
        {
        }

        /// <summary>
        ///     Gets identification number expected by client.
        /// </summary>
        [DataMember]
        public int Id { get; set; }

        /// <summary>
        ///     Gets a value indicating the configuration of the session through a serialized <see cref="ContentStore.Utils.DynamicJson"/>.
        /// </summary>
        [DataMember]
        public string? SerializedSessionConfiguration { get; set; }

        /// <summary>
        ///     Gets a value indicating the PAT the the session will use.
        /// </summary>
        [DataMember]
        public string? Pat { get; set; }

        /// <summary>
        ///     Gets set of strong fingerprints that are pending publishing.
        /// </summary>
        [DataMember]
        public IList<PublishingOperation>? PendingPublishingOperations { get; set; }
    }
}

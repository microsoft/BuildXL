// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Various operational modes of HistoricMetadataCache.
    /// </summary>
    public enum HistoricMetadataCacheMode
    {
        /// <summary>
        /// Disables both the metadata and hash to hash lookup
        /// </summary>
        Disable,

        /// <summary>
        /// Enables both the metadata cache and the Hash to Hash lookup
        /// </summary>
        HashToHashAndMetadata,

        /// <summary>
        /// Enables Hash to Hash lookup functionality within the Historic Metadata Cache.
        /// </summary>
        HashToHashOnly
    }
}
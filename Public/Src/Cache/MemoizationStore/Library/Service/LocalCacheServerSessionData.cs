// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Service
{
    /// <nodoc />
    public class LocalCacheServerSessionData : LocalContentServerSessionData
    {
        /// <nodoc />
        public string Pat { get; set; }

        /// <nodoc />
        public PublishingCacheConfiguration PublishingConfig { get; set; }

        /// <nodoc />
        public IList<PublishingOperation> PendingPublishingOperations { get; set; }

        /// <nodoc />
        public LocalCacheServerSessionData(
            string name,
            Capabilities capabilities,
            ImplicitPin implicitPin,
            IList<string> pins,
            string pat,
            PublishingCacheConfiguration publishingConfig,
            IList<PublishingOperation> pendingPublishingOperations)
            : base(name, capabilities, implicitPin, pins)
        {
            Pat = pat;
            PublishingConfig = publishingConfig;
            PendingPublishingOperations = pendingPublishingOperations;
        }

        /// <nodoc />
        public LocalCacheServerSessionData(LocalContentServerSessionData other)
            : base(other)
        {
        }
    }
}

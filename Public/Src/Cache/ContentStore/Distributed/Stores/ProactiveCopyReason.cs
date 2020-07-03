// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <nodoc />
    public enum ProactiveCopyReason
    {
        /// <nodoc />
        None,

        /// <nodoc />
        Pin,

        /// <nodoc />
        Put,

        /// <nodoc />
        Replication
    }
}

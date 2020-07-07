// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <nodoc />
    public enum CopyReason
    {
        /// <nodoc />
        None,

        /// <nodoc />
        Pin,

        /// <nodoc />
        Put,

        /// <nodoc />
        Replication,

        /// <nodoc />
        Place,

        /// <nodoc />
        OpenStream,

        /// <nodoc />
        AsyncPin,

        /// <nodoc />
        CentralStorage,
    }
}

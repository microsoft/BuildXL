// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <nodoc />
    /// <remarks>
    /// This enumeration is sorted ascending priority order for copy scheduling purposes.
    /// </remarks>
    public enum CopyReason
    {
        /// <nodoc />
        None,

        /// <nodoc />
        ProactiveBackground,

        /// <nodoc />
        ProactiveCopyOnPut,

        /// <nodoc />
        AsyncCopyOnPin,

        /// <nodoc />
        CentralStorage,

        /// <nodoc />
        ProactiveCopyOnPin,

        /// <nodoc />
        OpenStream,

        /// <nodoc />
        Place,

        /// <nodoc />
        Pin,
    }
}

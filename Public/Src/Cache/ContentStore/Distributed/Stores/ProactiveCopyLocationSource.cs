// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <nodoc />
    /// <remarks>
    /// This enumeration is sorted ascending priority order for copy scheduling purposes.
    /// </remarks>
    public enum ProactiveCopyLocationSource
    {
        /// <nodoc />
        None,

        /// <nodoc />
        Random,

        /// <nodoc />
        DesignatedLocation,
    }
}

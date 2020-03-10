// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <nodoc />
    [Flags]
    public enum ProactiveCopyMode
    {
        /// <nodoc />
        Disabled = 0,

        /// <nodoc />
        InsideRing = 1,

        /// <nodoc />
        OutsideRing = 2,

        /// <nodoc />
        Both = InsideRing | OutsideRing,
    }
}

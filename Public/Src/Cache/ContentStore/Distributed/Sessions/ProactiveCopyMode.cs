// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Distributed.Sessions
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
        Both = 3
    }
}

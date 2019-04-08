// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Time
{
    /// <summary>
    ///     Mockable clock interface to aid in unit testing
    /// </summary>
    public interface IClock
    {
        /// <summary>
        ///     Gets current UTC time
        /// </summary>
        DateTime UtcNow { get; }
    }
}

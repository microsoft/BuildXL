// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Time
{
    /// <summary>
    ///     System clock
    /// </summary>
    public class SystemClock : IClock
    {
        /// <summary>
        ///     Singleton instance for all to use
        /// </summary>
        public static readonly SystemClock Instance = new SystemClock();

        /// <summary>
        ///     Initializes a new instance of the <see cref="SystemClock"/> class.
        /// </summary>
        protected SystemClock()
        {
        }

        /// <inheritdoc />
        public DateTime UtcNow => DateTime.UtcNow;
    }
}

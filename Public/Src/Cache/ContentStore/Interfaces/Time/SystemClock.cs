// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

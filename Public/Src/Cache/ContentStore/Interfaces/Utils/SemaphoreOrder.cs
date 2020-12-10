// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Interfaces.Utils
{
    /// <summary>
    /// Order in which threads will enter the semaphore when using OrderedSemaphore.
    /// </summary>
    public enum SemaphoreOrder
    {
        /// <summary>
        /// Implies that the ordered semaphore will use the underlying semaphore's order.
        /// </summary>
        NonDeterministic,

        /// <nodoc />
        FIFO,

        /// <nodoc />
        LIFO,
    }
}

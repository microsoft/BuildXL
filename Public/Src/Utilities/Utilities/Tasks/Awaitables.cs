// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Helper class that exposes different awaitable types useful for asynchronous programming.
    /// </summary>
    public static class Awaitables
    {
        /// <summary>
        /// Enforces the rest of an async method to run in a thread pool's thread.
        /// </summary>
        public static HopToThreadPoolAwaitable ToThreadPool() => new HopToThreadPoolAwaitable();
    }
}

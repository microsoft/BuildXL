// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

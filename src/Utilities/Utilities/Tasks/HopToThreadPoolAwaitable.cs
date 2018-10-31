// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Custom awaiter that enforces the continuation to run in the thread-pool's thread.
    /// </summary>
    /// <remarks>
    /// In some cases an asynchronous method should be truly asynchronous.
    /// To do that you may use <code>Task.Run</code> or the following awaiter: <code>await new HopToThreadPoolAwaitable()</code> at the beginning of the method.
    /// </remarks>
    public readonly struct HopToThreadPoolAwaitable : INotifyCompletion
    {
        /// <nodoc />
        public HopToThreadPoolAwaitable GetAwaiter() => this;

        /// <nodoc />
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Awaitable pattern")]
        public bool IsCompleted => false;

        /// <nodoc />
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Awaitable pattern")]
        public void OnCompleted(Action continuation) => Task.Run(continuation);

        /// <nodoc />
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Awaitable pattern")]
        public void GetResult() { }
    }
}

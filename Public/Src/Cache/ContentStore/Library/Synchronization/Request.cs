// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Synchronization
{
    /// <summary>
    ///     Synchronization wrapper for signaling that an operation related to a particular context has been satisfied.
    /// </summary>
    public class Request<TArg, TResult>
    {
        private static long _id;

        /// <summary>
        ///     The argument for the request.
        /// </summary>
        public readonly TArg Value;

        private readonly TaskSourceSlim<TResult> _tcs = TaskSourceSlim.Create<TResult>();

        /// <summary>
        ///     Initializes a new instance of the <see cref="Request{TArg, TResult}"/> class.
        /// </summary>
        public Request(TArg value)
        {
            Id = Interlocked.Increment(ref _id);
            Value = value;
        }

        /// <summary>
        ///     Gets unique number for this request.
        /// </summary>
        public long Id { get; }

        /// <summary>
        ///     Asynchronously waits until the request has been completed.
        /// </summary>
        /// <returns>A task which blocks until the Request is signaled as having completed successfully, unsuccessfully, or throws.</returns>
        public Task<TResult> WaitForCompleteAsync() => _tcs.Task;

        /// <summary>
        ///     Signals the Request as being complete with the given result value.
        /// </summary>
        public void Complete(TResult result) => _tcs.TrySetResult(result);
    }
}

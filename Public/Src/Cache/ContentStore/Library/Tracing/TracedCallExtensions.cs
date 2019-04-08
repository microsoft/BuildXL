// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Tracing
{
    internal static class TracedCallExtensions
    {
        /// <summary>
        /// Runs a given <paramref name="runFunction"/> and ignores <see cref="ObjectDisposedException"/> that occurred in that function.
        /// </summary>
        public static async Task<TResult> RunSafeAsync<TTracer, TResult>(this TracedCall<TTracer, TResult> tracer, Func<Task<TResult>> runFunction) where TResult : ResultBase
        {
            // Unfortunately, there is no simple way to clean-up sessions and store in the right order.
            // The lifetime of these tightly coupled instances is separate and it is completely possible to shutdown session first
            // and then to call PutFile or PlaceFile methods on FileSystemContentStoreInternal that will use disposed PinContext.
            // The following code sets the cancellation flag on the result if the operation failed with ObjectDisposedException because PinContext instance was disposed.
            var result = await tracer.RunAsync(runFunction);
            if (result.HasException && result.Exception.IsPinContextObjectDisposedException())
            {
                result.IsCancelled = true;
            }

            return result;
        }
    }
}

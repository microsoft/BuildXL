// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <summary>
    /// Contains extension methods for <see cref="System.Threading.Channels.Channel"/> types.
    /// </summary>
    public static class ChannelsExtensions
    {
        /// <summary>
        /// Returns a <see cref="ValueTask{T}"/>that will complete when data is available to read or a given <paramref name="token"/> is canceled.
        /// </summary>
        /// <remarks>
        /// In most cases the channel reader just reads the data in the 'while' loop and should stop reading when the channel is closed or when
        /// the cancellation token is canceled. This helper function simplifies the logic by handling <see cref="OperationCanceledException"/> and just returning <code>false</code>
        /// if the cancellation was requested.
        /// </remarks>
        public static async ValueTask<bool> WaitToReadOrCanceledAsync<T>(this Channel<T> channel, CancellationToken token)
        {
            try
            {
                return await channel.Reader.WaitToReadAsync(token);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
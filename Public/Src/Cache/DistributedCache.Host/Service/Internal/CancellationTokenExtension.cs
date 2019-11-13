// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Cache.Host.Service.Internal
{
    internal static class CancellationTokenExtension
    {
        public static async Task WaitForCancellationAsync(this CancellationToken token)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (TaskCanceledException)
            {
                
            }
        }
    }
}

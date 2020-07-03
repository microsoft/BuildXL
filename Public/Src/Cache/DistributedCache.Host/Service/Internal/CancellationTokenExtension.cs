// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

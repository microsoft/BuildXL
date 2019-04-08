// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     Useful extensions to IContentSession
    /// </summary>
    public static class ContentSessionExtensions
    {
        /// <summary>
        ///     Ensure the existence of all related content by pinning it.
        /// </summary>
        public static async Task<bool> EnsureContentIsAvailableAsync(this IContentSession contentSession, Context context, ContentHashList contentHashList, CancellationToken cts)
        {
            // If there is no contentSession in which to find content, then trivially no content is available.
            if (contentSession == null)
            {
                return false;
            }

            // If the contentHashList does not exist, then trivially all content is pinned.
            if (contentHashList == null)
            {
                return true;
            }

            IEnumerable<Task<Indexed<PinResult>>> pinResultEnumerable = await contentSession.PinAsync(context, contentHashList.Hashes, cts).ConfigureAwait(false);
            var pinSucceeded = true;
            foreach (var pinResultTask in pinResultEnumerable)
            {
                var pinResult = await pinResultTask.ConfigureAwait(false);
                if (!pinResult.Item.Succeeded)
                {
                    if (pinResult.Item.Code != PinResult.ResultCode.ContentNotFound)
                    {
                        context.Warning($"Pinning hash {contentHashList.Hashes[pinResult.Index]} failed with error {pinResult}");
                    }

                    pinSucceeded = false;
                }
            }

            return pinSucceeded;
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// <remarks>
        /// On error, returns the first unsuccesful pin result.
        /// </remarks>
        public static async Task<PinResult> EnsureContentIsAvailableWithResultAsync(
            this IContentSession contentSession,
            Context context,
            string componentName,
            ContentHashList contentHashList,
            bool automaticallyOverwriteContentHashLists,
            CancellationToken cts)
        {
            // If there is no contentSession in which to find content or automatic overriding is turned off, then trivially no content is available.
            if (contentSession == null || !automaticallyOverwriteContentHashLists)
            {
                return PinResult.ContentNotFound;
            }

            // If the contentHashList does not exist, then trivially all content is pinned.
            if (contentHashList == null)
            {
                return PinResult.Success;
            }

            IEnumerable<Task<Indexed<PinResult>>> pinResultEnumerable = await contentSession.PinAsync(context, contentHashList.Hashes, cts).ConfigureAwait(false);

            foreach (var pinResultTask in pinResultEnumerable)
            {
                var pinResult = await pinResultTask.ConfigureAwait(false);
                if (!pinResult.Item.Succeeded)
                {
                    if (pinResult.Item.Code != PinResult.ResultCode.ContentNotFound)
                    {
                        context.Warning($"Pinning hash {contentHashList.Hashes[pinResult.Index]} failed with error {pinResult}", component: componentName);
                    }

                    return pinResult.Item;
                }
            }

            return PinResult.Success;
        }

        /// <summary>
        /// <see cref="EnsureContentIsAvailableWithResultAsync(IContentSession, Context, string, ContentHashList, bool, CancellationToken)"/>
        /// </summary>
        public static async Task<bool> EnsureContentIsAvailableAsync(this IContentSession contentSession, Context context, string componentName, ContentHashList contentHashList, bool automaticallyOverwriteContentHashLists, CancellationToken cts)
        {
            var pinResult = await EnsureContentIsAvailableWithResultAsync(contentSession, context, componentName, contentHashList, automaticallyOverwriteContentHashLists, cts).ConfigureAwait(false);
            return pinResult.Succeeded;
        }
    }
}

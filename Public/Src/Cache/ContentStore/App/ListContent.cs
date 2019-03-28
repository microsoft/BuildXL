// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Stores;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     List content verb.
        /// </summary>
        [Verb(Aliases = "lc", Description = "List content entries")]
        public void ListContent([Required, Description("Content cache root directory")] string root)
        {
            RunFileSystemContentStoreInternal(new AbsolutePath(root), async (context, store) =>
            {
                IReadOnlyList<ContentInfo> contentInfoList = await store.EnumerateContentInfoAsync().ConfigureAwait(false);
                foreach (var contentInfo in contentInfoList)
                {
                    _logger.Always($"Hash=[{contentInfo.ContentHash} Size=[{contentInfo.Size}]");
                }

                _logger.Always($"Directory has {contentInfoList.Count} entries");
            });
        }
    }
}

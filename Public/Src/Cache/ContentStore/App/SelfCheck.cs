// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     Purge verb.
        /// </summary>
        [Verb(Description = "Checks that the cache content is correct")]
        internal void SelfCheck([Required, Description("Content cache root directory")] string root)
        {
            RunFileSystemContentStoreInternal(new AbsolutePath(root), async (context, store) =>
            {
                context.Always($"Running self-check for '{root}'.");
                var result = await store.SelfCheckContentDirectoryAsync(context, CancellationToken.None).ConfigureAwait(false);
                context.Always($"Self check completed {result}.");
            });
        }
    }
}

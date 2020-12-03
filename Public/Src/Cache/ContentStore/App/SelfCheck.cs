// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
                _tracer.Always(context, $"Running self-check for '{root}'.");
                var result = await store.SelfCheckContentDirectoryAsync(context, CancellationToken.None).ConfigureAwait(false);
                _tracer.Always(context, $"Self check completed {result}.");
            });
        }
    }
}

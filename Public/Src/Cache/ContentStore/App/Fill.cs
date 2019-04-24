// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Utils;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     Fill verb.
        /// </summary>
        [Verb(Description = "Add random content to a store")]
        internal void Fill
            (
            [Required, Description("Content cache root directory")] string root,
            [Description(HashTypeDescription)] string hashType,
            [DefaultValue(null), Description("Size of content to add.")] string size,
            [DefaultValue(0), Description("Number of files to populate.")] int fileCount,
            [DefaultValue("4KB"), Description("Size of each populated file.")] string fileSize,
            [DefaultValue(false), Description("Whether to treat fileSize as exact or maximum.")] bool useExactSize
            )
        {
            var ht = GetHashTypeByNameOrDefault(hashType);

            RunFileSystemContentStore(new AbsolutePath(root), async (context, session) =>
            {
                if (size != null)
                {
                    await session.PutRandomAsync(context, ht, false, size.ToSize(), 100, fileSize.ToSize(), useExactSize)
                        .ConfigureAwait(false);
                }
                else
                {
                    await session.PutRandomAsync(context, ht, false, fileCount, fileSize.ToSize(), useExactSize)
                        .ConfigureAwait(false);
                }
            });
        }
    }
}

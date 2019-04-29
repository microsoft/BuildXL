// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Sessions;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     OpenStream verb
        /// </summary>
        [Verb(Description = "Open content from the store")]
        internal void OpenStream
            (
            [Description("Cache root directory path (using in-process cache)")] string cachePath,
            [Description("Cache name (using cache service)")] string cacheName,
            [Required, Description("Content hash value of referenced content to place")] string hash,
            [Description(HashTypeDescription)] string hashType,
            [Description("File name where the GRPC port can be found when using cache service. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName,
            [Description("The GRPC port."), DefaultValue(0)] int grpcPort
            )
        {
            var ht = GetHashTypeByNameOrDefault(hashType);
            var contentHash = new ContentHash(ht, HexUtilities.HexToBytes(hash));

            ServiceClientRpcConfiguration rpcConfig = null;
            if (cacheName != null)
            {
                if (grpcPort == 0)
                {
                    grpcPort = Helpers.GetGrpcPortFromFile(_logger, grpcPortFileName);
                }
                rpcConfig = new ServiceClientRpcConfiguration(grpcPort);
            }

            RunContentStore(cacheName, cachePath, rpcConfig, async (context, session) =>
            {
                var r = await session.OpenStreamAsync(context, contentHash, CancellationToken.None).ConfigureAwait(false);

                if (r.Succeeded)
                {
                    using (r.Stream)
                    {
                        var path = _fileSystem.GetTempPath() / $"{contentHash.ToHex()}.dat";
                        using (Stream fileStream = await _fileSystem.OpenSafeAsync(path, FileAccess.Write, FileMode.Create, FileShare.None))
                        {
                            await r.Stream.CopyToAsync(fileStream);
                            context.Always($"Content streamed to file path=[{path}]");
                        }
                    }
                }
                else
                {
                    context.Error(r.ToString());
                }
            });
        }
    }
}

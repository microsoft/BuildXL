// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.UtilitiesCore;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     PutStream verb
        /// </summary>
        [Verb(Description = "Put random content via a stream into the store")]
        internal void PutStream
        (
            [Description("Cache root directory path (using in-process cache)")] string cachePath,
            [Description("Cache name (using cache service)")] string cacheName,
            [DefaultValue(100), Description("Content size in bytes")] int size,
            [Description(HashTypeDescription)] string hashType,
            [Description("File name where the GRPC port can be found when using cache service. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName,
            [Description("The GRPC port."), DefaultValue(0)] int grpcPort
        )
        {
            var ht = GetHashTypeByNameOrDefault(hashType);

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
                using (var stream = new MemoryStream(ThreadSafeRandom.GetBytes(size)))
                {
                    PutResult result = await session.PutStreamAsync(
                        context, ht, stream, CancellationToken.None).ConfigureAwait(false);

                    if (!result.Succeeded)
                    {
                        context.Error(result.ToString());
                    }
                    else
                    {
                        context.Always($"Content added with hash=[{result.ContentHash}], size=[{result.ContentSize}]");
                    }
                }
            });
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     PutFile verb
        /// </summary>
        [Verb(Description = "Put content in a file at the specified path into the store")]
        internal void PutFile
            (
            [Description("Cache root directory path (using in-process cache)")] string cachePath,
            [Description("Cache name (using cache service)")] string cacheName,
            [Required, Description("Path to destination file")] string path,
            [Description(HashTypeDescription)] string hashType,
            [DefaultValue(FileRealizationMode.Any)] FileRealizationMode realizationMode,
            [DefaultValue(false), Description("Stream bytes if true")] bool useStream,
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
                BoolResult result;
                ContentHash contentHash;
                long contentSize;

                if (useStream)
                {
                    using (var stream = File.OpenRead(path))
                    {
                        var r = await session.PutStreamAsync(context, ht, stream, CancellationToken.None).ConfigureAwait(false);
                        contentHash = r.ContentHash;
                        contentSize = r.ContentSize;
                        result = r;
                    }
                }
                else
                {
                    var r = await session.PutFileAsync(
                        context, ht, new AbsolutePath(path), realizationMode, CancellationToken.None).ConfigureAwait(false);
                    contentHash = r.ContentHash;
                    contentSize = r.ContentSize;
                    result = r;
                }

                if (!result.Succeeded)
                {
                    context.Error(result.ToString());
                }
                else
                {
                    context.Always($"Content added with hash=[{contentHash}], size=[{contentSize}]");
                }
            });
        }
    }
}

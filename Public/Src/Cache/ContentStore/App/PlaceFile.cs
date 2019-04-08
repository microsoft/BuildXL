// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        ///     PlaceFile verb
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "cachePath")]
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "cacheName")]
        [Verb(Description = "Place content from the store to a file at the specified path")]
        internal void PlaceFile
            (
            [Description("Cache root directory path (using in-process cache)")] string cachePath,
            [Description("Cache name (using cache service)")] string cacheName,
            [Required, Description("Content hash value of referenced content to place")] string hash,
            [Required, Description("Path to destination file")] string path,
            [Description(HashTypeDescription)] string hashType,
            [DefaultValue(FileAccessMode.ReadOnly)] FileAccessMode accessMode,
            [DefaultValue(FileReplacementMode.ReplaceExisting)] FileReplacementMode replacementMode,
            [DefaultValue(FileRealizationMode.HardLink)] FileRealizationMode realizationMode,
            [DefaultValue(false), Description("Stream bytes if true")] bool useStream,
            [Description("File name where the GRPC port can be found when using cache service. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName,
            [Description("The GRPC port."), DefaultValue(0)] int grpcPort
            )
        {
            var ht = GetHashTypeByNameOrDefault(hashType);
            var contentHash = new ContentHash(ht, HexUtilities.HexToBytes(hash));
            var filePath = new AbsolutePath(path);

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
                if (useStream)
                {
                    var r = await session.OpenStreamAsync(context, contentHash, CancellationToken.None).ConfigureAwait(false);

                    if (r.Succeeded)
                    {
                        using (r.Stream)
                        {
                            using (var fileStream = File.OpenWrite(filePath.Path))
                            {
                                await r.Stream.CopyToAsync(fileStream);
                                context.Always("Success");
                            }
                        }
                    }
                    else
                    {
                        context.Error(r.ToString());
                    }
                }
                else
                {
                    var r = await session.PlaceFileAsync(
                        context,
                        contentHash,
                        filePath,
                        accessMode,
                        replacementMode,
                        realizationMode,
                        CancellationToken.None).ConfigureAwait(false);

                    if (!r.Succeeded)
                    {
                        context.Error(r.ToString());
                    }
                    else
                    {
                        context.Always("Success");
                    }
                }
            });
        }
    }
}

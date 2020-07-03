// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// ReSharper disable once UnusedMember.Global

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Service.Grpc;
using CLAP;

namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        [Verb(Description = "Remove content from the cache")]
        internal void Delete
            (
            [Required, Description(GrpcPortDescription)] int grpcPort,
            [Required, Description(HashTypeDescription)] string hashType,
            [Required, Description("Content hash value of referenced content to place")] string hash
            )
        {
            Initialize();

            var context = new Interfaces.Tracing.Context(_logger);

            try
            {
                Validate();

                var ht = GetHashTypeByNameOrDefault(hashType);
                var contentHash = new ContentHash(ht, HexUtilities.HexToBytes(hash));

                GrpcContentClient client = new GrpcContentClient(
                    new Sessions.ServiceClientContentSessionTracer(nameof(Delete)),
                    _fileSystem,
                    grpcPort,
                    _scenario);
                
                var deleteResult = client.DeleteContentAsync(context, contentHash, deleteLocalOnly: false).GetAwaiter().GetResult();
                _tracer.Always(context, deleteResult.ToString());
            }
            catch (Exception e)
            {
                _tracer.Error(context, e, $"Unhandled exception in {nameof(Application)}.{nameof(Delete)}");
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// ReSharper disable once UnusedMember.Global

using System;
using System.Linq;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using CLAP;

namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        [Verb(Description = "Remove content from the cache")]
        internal void Delete
            (
            [Required, Description(HashTypeDescription)] string hashType,
            [Required, Description("Content hash value of referenced content to place")] string hash,
            [Optional, Description(GrpcPortDescription), DefaultValue(GrpcConstants.DefaultEncryptedGrpcPort)] int grpcPort,
            [Optional, Description("Whether to enable encryption"), DefaultValue(true)] bool encrypt
            )
        {
            Initialize();
            
            // We need to initialize this here
            GrpcEnvironment.Initialize();

            var context = new Interfaces.Tracing.Context(_logger);
            var operationContext = new OperationContext(context);

            try
            {
                Validate();

                var ht = GetHashTypeByNameOrDefault(hashType);
                var contentHash = new ContentHash(ht, HexUtilities.HexToBytes(hash));

                GrpcContentClient client = new GrpcContentClient(
                    new OperationContext(context),
                    new ServiceClientContentSessionTracer(nameof(Delete)),
                    _fileSystem,
                    new ServiceClientRpcConfiguration(grpcPort)
                    {
                        GrpcCoreClientOptions = new()
                        {
                            EncryptionEnabled = encrypt,
                        }
                    },
                    _scenario);
                
                var deleteResult = client.DeleteContentAsync(operationContext, contentHash, deleteLocalOnly: false).GetAwaiter().GetResult();
                _tracer.Always(context, deleteResult.ToString());
            }
            catch (Exception e)
            {
                _tracer.Error(context, e, $"Unhandled exception in {nameof(Application)}.{nameof(Delete)}");
            }
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Stores;
using ContentStoreTest.Stores;
using FluentAssertions;
using Microsoft.Practices.TransientFaultHandling;

namespace ContentStoreTest.Sessions
{
    public class TestServiceClientContentSession : ServiceClientContentSession
    {
        private readonly AbsolutePath _rootPath;

        public TestServiceClientContentSession(
            string name,
            ImplicitPin implicitPin,
            RetryPolicy retryPolicy,
            AbsolutePath rootPath,
            string cacheName,
            ILogger logger,
            IAbsFileSystem fileSystem,
            string scenario,
            ITestServiceClientContentStore store,
            ServiceClientContentSessionTracer sessionTracer,
            ServiceClientRpcConfiguration rpcConfiguration)
            : base(name, implicitPin, logger, fileSystem, sessionTracer, new ServiceClientContentStoreConfiguration(cacheName, rpcConfiguration, scenario, retryPolicy))
        {
            _rootPath = rootPath;
            Store = store;
        }

        public ITestServiceClientContentStore Store { get; }

        public void DisableHeartbeat()
        {
            var grpcClient = RpcClient as GrpcContentClient;
            grpcClient.Should().NotBeNull("Only grpc clients can have a heartbeat.");

            grpcClient?.DisableCapabilities(Capabilities.Heartbeat);
        }

        public Task<IReadOnlyList<ContentHash>> EnumerateHashes()
        {
            var contentHashes = new List<ContentHash>();

            foreach (var fileInfo in FileSystem.EnumerateFiles(_rootPath, EnumerateOptions.Recurse))
            {
                AbsolutePath path = fileInfo.FullPath;
                string filename = path.FileName;

                var i = filename.LastIndexOf(".blob", StringComparison.CurrentCultureIgnoreCase);
                if (i > 0)
                {
                    var hashName = path.Parent.Parent.FileName;
                    var hashType = (HashType)Enum.Parse(typeof(HashType), hashName, true);

                    var contentHashHex = filename.Substring(0, filename.Length - 5);
                    var contentHash = new ContentHash(hashType, HexUtilities.HexToBytes(contentHashHex));
                    contentHashes.Add(contentHash);
                }
            }

            return Task.FromResult((IReadOnlyList<ContentHash>)contentHashes);
        }
    }
}

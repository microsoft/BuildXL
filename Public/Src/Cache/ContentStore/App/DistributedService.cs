// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using CLAP;
using Newtonsoft.Json;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        /// Run the distributed service verb.
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Run distributed CAS service")]
        internal void DistributedService
            (
            [Description("Path to DistributedContentSettings file")] string settingsPath,
            [Description("Cache name")] string cacheName,
            [Description("Cache root path")] string cachePath,
            [DefaultValue((int)ServiceConfiguration.GrpcDisabledPort), Description(GrpcPortDescription)] int grpcPort,
            [Description("Name of the memory mapped file used to share GRPC port. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName,
            [DefaultValue(null), Description("Writable directory for service operations (use CWD if null)")] string dataRootPath,
            [DefaultValue(null), Description("Identifier for the stamp this service will run as")] string stampId,
            [DefaultValue(null), Description("Identifier for the ring this service will run as")] string ringId,
            [DefaultValue(Constants.OneMB), Description("Max size quota in MB")] int maxSizeQuotaMB,
            [DefaultValue(false)] bool debug,
            [DefaultValue(false), Description("Whether or not GRPC is used for file copies")] bool useDistributedGrpc,
            [DefaultValue(false), Description("Whether or not GZip is used for GRPC file copies")] bool useCompressionForCopies,
            [DefaultValue(null), Description("Buffer size for streaming GRPC copies")] int? bufferSizeForGrpcCopies
            )
        {
            Initialize();

            if (debug)
            {
                System.Diagnostics.Debugger.Launch();
            }

            try
            {
                var cancellationTokenSource = new CancellationTokenSource();

                // IMPORTANT
                // 
                // gRPC server also registers a CancelKeyPress handler, which is why we must:
                //   - register ours before initializing gRPC server (so that we get called first)
                //   - cancel the event once we handle it, so that gRPC's handler doesn't get called
                //
                // If both handlers are invoked, then once we call _grpcServer.KillAsync() in response
                // to this event, the gRPC server will have already shut down (because of its own response
                // to this event) and so we calling KillAsync() again instantly (and silently) kills the
                // whole program (at least on .NET Core on Mac).  This happens somewhere in native gRPC
                // code, so everything looks like the program exited normally, when in fact the program
                // exited inside of _grpcServer.KillAsync() and the rest of our cleanup code (e.g., 
                // Application.Dispose()) was never executed.
                Console.CancelKeyPress += (sender, args) =>
                {
                    args.Cancel = true;
                    cancellationTokenSource.Cancel();
                };

                var dcs = JsonConvert.DeserializeObject<DistributedContentSettings>(File.ReadAllText(settingsPath));

                var host = new HostInfo(stampId, ringId, new List<string>());

                if (grpcPort == 0)
                {
                    grpcPort = Helpers.GetGrpcPortFromFile(_logger, grpcPortFileName);
                }

                var arguments = CreateDistributedCacheServiceArguments(
                    copier: useDistributedGrpc
                        ? new GrpcFileCopier(
                            context: new Interfaces.Tracing.Context(_logger),
                            grpcPort: grpcPort,
                            maxGrpcClientCount: dcs.MaxGrpcClientCount,
                            maxGrpcClientAgeMinutes: dcs.MaxGrpcClientAgeMinutes,
                            grpcClientCleanupDelayMinutes: dcs.GrpcClientCleanupDelayMinutes,
                            useCompression: useCompressionForCopies,
                            bufferSize: bufferSizeForGrpcCopies)
                        : (IAbsolutePathFileCopier)new DistributedCopier(),
                    pathTransformer: useDistributedGrpc ? new GrpcDistributedPathTransformer() : (IAbsolutePathTransformer)new DistributedPathTransformer(),
                    dcs: dcs,
                    host: host,
                    cacheName: cacheName,
                    cacheRootPath: cachePath,
                    grpcPort: (uint)grpcPort,
                    maxSizeQuotaMB: maxSizeQuotaMB,
                    dataRootPath: dataRootPath,
                    ct: cancellationTokenSource.Token,
                    bufferSizeForGrpcCopies: bufferSizeForGrpcCopies);

                DistributedCacheServiceFacade.RunAsync(arguments).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private class EnvironmentVariableHost : IDistributedCacheServiceHost
        {
            public string GetSecretStoreValue(string key)
            {
                return Environment.GetEnvironmentVariable(key);
            }

            public void OnStartedService()
            {
            }

            public Task OnStartingServiceAsync()
            {
                return Task.CompletedTask;
            }

            public void OnTeardownCompleted()
            {
            }

            public Task<Dictionary<string, string>> RetrieveKeyVaultSecretsAsync(List<string> secrets, CancellationToken token)
            {
                return Task.FromResult(secrets.ToDictionary(s => GetSecretStoreValue(s)));
            }
        }
    }
}

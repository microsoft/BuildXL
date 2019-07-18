// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;

namespace BuildXL.Cache.ContentStore.Vfs
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using BuildXL.Cache.ContentStore.Interfaces.Extensions;
    using BuildXL.Cache.ContentStore.Interfaces.Logging;
    using BuildXL.Cache.ContentStore.Interfaces.Results;
    using BuildXL.Cache.ContentStore.Interfaces.Stores;
    using BuildXL.Cache.ContentStore.Interfaces.Tracing;
    using BuildXL.Cache.ContentStore.Tracing;
    using BuildXL.Cache.ContentStore.Tracing.Internal;
    using BuildXL.Native.IO;
    using static Placeholder;

    /// <summary>
    ///     Entry point of the application.
    /// </summary>
    public class VfsCasRunner
    {
        private Tracer Tracer { get; } = new Tracer(nameof(VfsCasRunner));

        public async Task RunAsync(VfsCasConfiguration configuration)
        {
            // Create VFS root
            using (var fileLog = new FileLog((configuration.RootPath / "Bvfs.log").Path))
            using (var logger = new Logger(fileLog))
            {
                var fileSystem = new PassThroughFileSystem(logger);
                var context = new OperationContext(new Context(logger));

                // Map junctions into VFS root
                foreach (var mount in configuration.VirtualizationMounts)
                {
                    CreateJunction(context, source: mount.Value, target: configuration.VfsMountRootPath / mount.Key);
                }

                var clientContentStore = new ServiceClientContentStore(
                    logger,
                    fileSystem,
                    new ServiceClientContentStoreConfiguration(
                        configuration.CacheName,
                        new ServiceClientRpcConfiguration(
                            configuration.BackingGrpcPort),
                        scenario: configuration.Scenario));

                // Client is startup/shutdown with wrapping VFS content store

                using (var server = new LocalContentServer(
                    fileSystem,
                    logger,
                    scenario: "bvfs" + configuration.ServerGrpcPort,
                    path => new VirtualizedContentStore(clientContentStore, logger, configuration),
                    new LocalServerConfiguration(
                        configuration.DataRootPath,
                        new Dictionary<string, AbsolutePath>()
                        {
                            { configuration.CacheName, configuration.ServerRootPath }
                        },
                        configuration.ServerGrpcPort)))
                {
                    await server.StartupAsync(context).ThrowIfFailure();

                    await WaitForTerminationAsync(context);

                    await server.ShutdownAsync(context).ThrowIfFailure();
                }
            }
        }

        private static Task WaitForTerminationAsync(Context context)
        {
            var termination = new TaskCompletionSource<bool>();

            Console.CancelKeyPress += (sender, args) =>
            {
                context.Debug("Terminating due to cancellation request on console.");
                termination.TrySetResult(true);
            };

            Task.Run(() =>
            {
                string line = null;
                while ((line = Console.ReadLine()) != null)
                {
                    if (line.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Debug("Terminating due to exit request on console.");
                        termination.TrySetResult(true);
                        break;
                    }
                }

                context.Debug("Terminating due to end of standard input.");
                termination.TrySetResult(true);
            }).FireAndForget(context);

            return termination.Task;
        }

        private void CreateJunction(OperationContext context, AbsolutePath source, AbsolutePath target)
        {
            context.PerformOperation(
                Tracer,
                () =>
                {
                    Directory.CreateDirectory(target.Path);
                    Directory.CreateDirectory(source.Path);
                    FileUtilities.CreateJunction(source.Path, target.Path);
                    return BoolResult.Success;
                },
                extraStartMessage: $"{source}=>{target}").ThrowIfFailure();
        }
    }
}

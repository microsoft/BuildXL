// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Native.IO;

namespace BuildXL.Cache.ContentStore.Vfs
{
    /// <summary>
    ///     Entry point of the application.
    /// </summary>
    public class VfsCasRunner
    {
        private Tracer Tracer { get; } = new Tracer(nameof(VfsCasRunner));

        public async Task RunAsync(VfsServiceConfiguration configuration)
        {
            // Create VFS root
            using (var fileLog = new FileLog((configuration.CasConfiguration.RootPath / "Bvfs.log").Path))
            using (var logger = new Logger(fileLog))
            {
                var fileSystem = new PassThroughFileSystem(logger);
                var context = new OperationContext(new Context(logger));

                // Map junctions into VFS root
                foreach (var mount in configuration.CasConfiguration.VirtualizationMounts)
                {
                    CreateJunction(context, source: mount.Value, target: configuration.CasConfiguration.VfsMountRootPath / mount.Key);
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
                    path => new VirtualizedContentStore(clientContentStore, logger, configuration.CasConfiguration),
                    new LocalServerConfiguration(
                        configuration.CasConfiguration.DataRootPath,
                        new Dictionary<string, AbsolutePath>()
                        {
                            { configuration.CacheName, configuration.ServerRootPath }
                        },
                        configuration.ServerGrpcPort,
                        fileSystem)))
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

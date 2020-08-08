// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Roxis.Common;

namespace BuildXL.Cache.Roxis.Server
{
    /// <summary>
    /// Roxis service spawning a Grpc server and backed by a local RocksDb
    /// </summary>
    public sealed class RoxisService : StartupShutdownSlimBase, IRoxisService
    {
        private readonly RoxisServiceConfiguration _configuration;
        private readonly ILogger _logger;

        private readonly RoxisGrpcService _grpcMetadataService;
        private readonly RoxisDatabase _database;

        protected override Tracer Tracer { get; } = new Tracer(nameof(RoxisService));

        private RoxisService(RoxisServiceConfiguration configuration, ILogger logger)
        {
            _configuration = configuration;
            _logger = logger;

            _grpcMetadataService = new RoxisGrpcService(_configuration.Grpc, this);
            _database = new RoxisDatabase(_configuration.Database, SystemClock.Instance);
        }

        public static async Task RunAsync(RoxisServiceConfiguration? configuration, ILogger logger, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            var tracingContext = new Context(logger);

            var service = new RoxisService(configuration ?? new RoxisServiceConfiguration(), logger);
            await service.StartupAsync(tracingContext).ThrowIfFailureAsync();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(service.ShutdownStartedCancellationToken, cancellationToken))
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
                }
                catch (TaskCanceledException)
                {

                }
            }

            await service.ShutdownAsync(tracingContext).ThrowIfFailureAsync();
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _database.StartupAsync(context).ThrowIfFailureAsync();
            await _grpcMetadataService.StartupAsync(context).ThrowIfFailureAsync();
            return BoolResult.Success;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await _grpcMetadataService.ShutdownAsync(context).ThrowIfFailureAsync();
            await _database.ShutdownAsync(context).ThrowIfFailureAsync();
            return BoolResult.Success;
        }

        public async Task<CommandResponse> HandleAsync(CommandRequest request)
        {
            var results = await Task.WhenAll(request.Commands.Select(c => _database.HandleAsync(c)));
            return new CommandResponse(results);
        }
    }
}

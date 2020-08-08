// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Roxis.Common;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.Roxis.Server;
using BuildXL.Cache.Roxis.Client;

namespace BuildXL.Cache.Roxis.Test
{
    /// <summary>
    /// <see cref="IRoxisClient"/> that is basically a shim over a <see cref="RoxisDatabase"/>. Used for removing Grpc
    /// from tests.
    /// </summary>
    public class LocalRoxisClient : StartupShutdownSlimBase, IRoxisClient
    {
        protected override Tracer Tracer => new Tracer(nameof(LocalRoxisClient));

        private readonly RoxisDatabase _database;

        public LocalRoxisClient(RoxisDatabase database)
        {
            _database = database;
        }

        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            return _database.StartupAsync(context);
        }

        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            return _database.ShutdownAsync(context);
        }

        public async Task<Result<CommandResponse>> ExecuteAsync(OperationContext context, CommandRequest request)
        {
            return await context.PerformOperationAsync(Tracer, async () =>
            {
                var results = await Task.WhenAll(request.Commands.Select(c => _database.HandleAsync(c)));
                return Result.Success(new CommandResponse(results));
            });
        }
    }
}

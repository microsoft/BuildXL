// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

public class GrpcContentTrackerClient : GrpcCodeFirstClient<IGrpcContentTracker>
{
    public record Configuration(TimeSpan OperationTimeout, RetryPolicyConfiguration RetryPolicy);

    protected override Tracer Tracer { get; } = new(nameof(GrpcContentTrackerClient));

    public GrpcContentTrackerClient(Configuration configuration, IClientAccessor<IGrpcContentTracker> accessor)
        : base(accessor, CreateRetryPolicy(configuration.RetryPolicy), SystemClock.Instance, configuration.OperationTimeout)
    {
    }

    private static IRetryPolicy CreateRetryPolicy(RetryPolicyConfiguration configurationRetryPolicy)
    {
        return configurationRetryPolicy.AsRetryPolicy(_ => true,
            // We use an absurdly high retry count because the actual operation timeout is controlled through
            // PerformOperationAsync in ExecuteAsync.
            1_000_000);
    }

    public Task<BoolResult> UpdateLocationsAsync(OperationContext context, UpdateLocationsRequest request)
    {
        return ExecuteAsync(
            context,
            async (context, options, service) =>
            {
                // TODO: what we really want is to have a background operation that sends these instead of doing tons of ops
                await service.UpdateLocationsAsync(request, options);
                return BoolResult.Success;
            },
            extraEndMessage: _ => $"Request=[{request}]");
    }

    public Task<Result<GetLocationsResponse>> GetLocationsAsync(OperationContext context, GetLocationsRequest request)
    {
        return ExecuteAsync(
            context,
            async (context, options, service) => Result.Success(await service.GetLocationsAsync(request, options)),
            extraEndMessage: result =>
                             {
                                 var response = string.Empty;
                                 if (result.Succeeded)
                                 {
                                     response = $" Response=[{result.Value}]";
                                 }

                                 return $"Request=[{request}]{response}";  
                             });
    }
}

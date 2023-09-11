// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService;

/// <summary>
/// Obtains a connection to a host that implements <typeparamref name="TService"/>
/// </summary>
/// <remarks>
/// The <typeparamref name="TService"/> may actually point to different hosts depending on <typeparamref name="TKey"/>
/// </remarks>
public interface IClientAccessor<TKey, out TService> : IStartupShutdownSlim
{
    /// <summary>
    /// Allows the callee to temporarily utilize an instance of <see cref="TService"/>.
    /// </summary>
    /// <remarks>
    /// The instance MUST NOT be stored anywhere. These instances may be pooled and shut down at any time outside of
    /// the scope of <see cref="operation"/>.
    /// </remarks>
    Task<TResult> UseAsync<TResult>(OperationContext context, TKey key, Func<TService, Task<TResult>> operation);
}

public static class ClientAccessorExtensions {
    public static async Task<TResult> WithClientAsync<TKey, TService, TRequest, TResult>(
        this IClientAccessor<TKey, TService> clients,
        OperationContext context,
        TRequest request,
        TKey location,
        Func<OperationContext, TService, TRequest, Task<TResult>> func,
        Counter success,
        Counter failure,
        Counter tally)
        where TResult : ResultBase
    {
        // We yield here to prevent the caller from blocking. This is important because the caller is likely issuing
        // multiple API requests, and the code below may need to establish a connection that might take some time.
        await Task.Yield();

        try
        {
            // TODO: retry?
            var result = await clients.UseAsync(
                context,
                location,
                contentTracker => func(context, contentTracker, request));

            if (result.Succeeded)
            {
                success.Increment();
            }
            else
            {
                failure.Increment();
            }

            return result;
        }
        catch (Exception exception)
        {
            failure.Increment();
            return (new ErrorResult(exception)).AsResult<TResult>();
        }
        finally
        {
            tally.Increment();
        }
    }
}

/// <summary>
/// Obtains a connection to a host that implements <typeparamref name="TService"/>.
/// </summary>
/// <remarks>
/// This can only be used to establish a connection to a specific host. Please note, "specific host" doesn't mean
/// "specific machine", it means that the host is not controlled by a key (and therefore may change over time). For
/// example, <see cref="MasterClientFactory{T}"/> connects to the current master of the cluster, whatever that may be.
///
/// In contrast, <see cref="FixedClientAccessor{TService}"/> and <see cref="DelayedFixedClientAccessor{TService}"/>
/// both connect to specific instances.
/// </remarks>
public interface IFixedClientAccessor<TService> : IStartupShutdownSlim
{
    /// <summary>
    /// Specific location of the host we're currently going to connect to if the callee calls
    /// <see cref="UseAsync{TResult}"/>.
    /// </summary>
    /// <remarks>
    /// This should only be used for telemetry, as the location may change over time.
    /// </remarks>
    public MachineLocation Location { get; }

    /// <summary>
    /// Allows the callee to temporarily utilize an instance of <see cref="TService"/>.
    /// </summary>
    /// <remarks>
    /// The instance MUST NOT be stored anywhere. These instances may be pooled and shut down at any time outside of
    /// the scope of <see cref="operation"/>.
    /// </remarks>
    Task<TResult> UseAsync<TResult>(OperationContext context, Func<TService, Task<TResult>> operation);
}

public class FixedClientAccessor<TService> : StartupShutdownComponentBase, IFixedClientAccessor<TService>
{
    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new(nameof(FixedClientAccessor<TService>));

    /// <inheritdoc />
    public MachineLocation Location { get; }

    private readonly TService _service;

    public FixedClientAccessor(TService service, MachineLocation location)
    {
        _service = service;
        Location = location;

        if (_service is IStartupShutdownSlim startupShutdownSlim)
        {
            LinkLifetime(startupShutdownSlim);
        }
    }

    protected override string GetArgumentsMessage()
    {
        return $"Location=[{Location}]";
    }

    /// <inheritdoc />
    public Task<TResult> UseAsync<TResult>(OperationContext context, Func<TService, Task<TResult>> operation)
    {
        return operation?.Invoke(_service);
    }
}

public class DelayedFixedClientAccessor<TService> : StartupShutdownComponentBase, IFixedClientAccessor<TService>
{
    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new(nameof(FixedClientAccessor<TService>));

    /// <inheritdoc />
    public MachineLocation Location { get; }

    private readonly AsyncLazy<TService> _service;

    public DelayedFixedClientAccessor(Func<Task<TService>> factory, MachineLocation location)
    {
        Location = location;
        _service = new AsyncLazy<TService>(factory);
    }

    protected override string GetArgumentsMessage()
    {
        return $"Location=[{Location}]";
    }

    /// <inheritdoc />
    public async Task<TResult> UseAsync<TResult>(OperationContext context, Func<TService, Task<TResult>> operation)
    {
        Contract.Requires(operation is not null);
        var service = await _service.GetValueAsync();
        return await operation.Invoke(service);
    }
}

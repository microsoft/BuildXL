// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Service;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.Host.Configuration.Test;

public class MockLauncherProcess : ILauncherProcess
{
    private readonly ServiceLifetimeManager _lifetimeManager;
    private readonly string _serviceId;
    private readonly bool _shutdownGracefully;

    private int _exitCode = 42;

    /// <nodoc />
    public MockLauncherProcess(ProcessStartInfo info, ServiceLifetimeManager lifetimeManager, string serviceId, bool shutdownGracefully)
    {
        ProcessStartInfo = info;
        _lifetimeManager = lifetimeManager;
        _serviceId = serviceId;
        _shutdownGracefully = shutdownGracefully;
    }

    public void SetExitCode(int exitCode) => _exitCode = exitCode;

    /// <nodoc />
    public ProcessStartInfo ProcessStartInfo { get; }

    /// <nodoc />
    public bool Started { get; private set; }

    /// <inheritdoc />
    public void Start(OperationContext context)
    {
        Started = true;

        // Tracking the lifetime of the process if lifetime manager was passed through the constructor.
        if (_lifetimeManager is not null)
        {
            // Detaching the cancellation, because we don't want to stop the following operation if the token was trigerred.
            context = context.WithoutCancellationToken();

            _lifetimeManager.ServiceStarted(context, _serviceId).ThrowIfFailure();

            if (_shutdownGracefully)
            {
                _lifetimeManager.WaitForShutdownRequestAsync(context, _serviceId)
                    .ContinueWith(
                        _ =>
                        {
                            _lifetimeManager.ServiceStopped(context, _serviceId).ThrowIfFailure();
                            OnExited();
                        }).Forget();
            }
        }
    }

    /// <inheritdoc />
    public event Action Exited;

    public Action KillAction;

    /// <inheritdoc />
    public void Kill(OperationContext context)
    {
        KillAction?.Invoke();
    }

    /// <inheritdoc />
    public int ExitCode { get; private set; }

    /// <inheritdoc />
    public int Id => 42;

    /// <inheritdoc />
    public bool HasExited { get; private set; }

    public void OnExited(int? exitCode = null)
    {
        ExitCode = exitCode ?? _exitCode;
        HasExited = true;
        Exited?.Invoke();
    }
}

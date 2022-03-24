// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// A wrapper around launched processed managed by <see cref="ServiceLifetimeManager"/>.
    /// </summary>
    internal sealed class LauncherManagedProcess 
    {
        private static readonly Tracer Tracer = new Tracer(nameof(LauncherManagedProcess));

        private readonly ILauncherProcess _process;
        private readonly ServiceLifetimeManager _lifetimeManager;
        private readonly TaskSourceSlim<int> _processExitSource = TaskSourceSlim.Create<int>();

        public LauncherManagedProcess(ILauncherProcess process, string serviceId, ServiceLifetimeManager lifetimeManager)
        {
            _process = process;
            _lifetimeManager = lifetimeManager;
            ServiceId = serviceId;
        }

        /// <nodoc />
        public string ServiceId { get; }

        /// <summary>
        /// Returns an underlying process that this class manages lifetime of.
        /// </summary>
        public ILauncherProcess Process => _process;

        /// <nodoc />
        public int ProcessId => _process.Id;

        /// <nodoc />
        public bool HasExited => _process.HasExited;

        /// <nodoc />
        public BoolResult Start(OperationContext context)
        {
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    _process.Start(context);
                    _process.Exited += () => OnExited(context, "Process Exited");
                    return Result.Success(_process.Id);
                },
                traceOperationStarted: true,
                extraStartMessage: $"ServiceId={ServiceId}",
                messageFactory: r => $"ProcessId={r.GetValueOrDefault(defaultValue: -1)}, ServiceId={ServiceId}"
            );
        }

        /// <nodoc />
        public Task<Result<int>> StopAsync(OperationContext context, TimeSpan shutdownTimeout)
        {
            bool alreadyExited = false;
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async nestedContext =>
                {
                    if (HasExited)
                    {
                        alreadyExited = true;
                        return Result.Success(_process.ExitCode);
                    }

                    // Terminating the process after timeout if it won't shutdown gracefully.
                    using var registration = nestedContext.Token.Register(
                        () =>
                        {
                            // It is important to pass 'context' and not 'nestedContext',
                            // because 'nestedContext' will be canceled at a time we call Kill method.
                            Kill(context);
                        });

                    // Trying to shut down the service gracefully and ignoring the error, because its already being traced).
                    await _lifetimeManager.GracefulShutdownServiceAsync(nestedContext, ServiceId).IgnoreFailure();

                    // Waiting for the process exit task. It should be set to completion either by a graceful shutdown,
                    // or by calling Kill method.
                    return await _processExitSource.Task;
                },
                timeout: shutdownTimeout,
                extraStartMessage: $"ProcessId={ProcessId}, ServiceId={ServiceId}",
                extraEndMessage: r => $"ProcessId={ProcessId}, ServiceId={ServiceId}, ExitCode={r.GetValueOrDefault(-1)}, AlreadyExited={alreadyExited}");
        }

        private void Kill(OperationContext context)
        {
            context
                .WithoutCancellationToken() // Not using the cancellation token from the context.
                .PerformOperation(
                    Tracer,
                    () =>
                    {
                        // Using Result<string> for tracing purposes.
                        if (HasExited)
                        {
                            OnExited(context, "TerminateServiceAlreadyExited");
                            return Result.Success("AlreadyExited");
                        }

                        _process.Kill(context);
                        return Result.Success("ProcessKilled");

                    },
                    extraStartMessage: $"ProcessId={ProcessId}, ServiceId={ServiceId}",
                    messageFactory: r => $"ProcessId={ProcessId}, ServiceId={ServiceId}, {r}")
                .IgnoreFailure();

            // Intentionally trying to set the result that indicates the cancellation after PerformOperation call that will never throw.
            _processExitSource.TrySetResult(-1);
        }

        private void OnExited(OperationContext context, string trigger)
        {
            // It is important to disable the cancellation here because in some cases the token associated
            // with the context can be set.
            // But the operation that we do here is very fast and we use context for tracing purposes only.
            context
                .WithoutCancellationToken()
                .PerformOperation(
                    Tracer,
                    () =>
                    {
                        _processExitSource.TrySetResult(_process.ExitCode);
                        return Result.Success(_process.ExitCode.ToString());
                    },
                    caller: "ServiceExited",
                    messageFactory: r => $"ProcessId={ProcessId}, ServiceId={ServiceId}, ExitCode={r.GetValueOrDefault(string.Empty)} Trigger={trigger}")
                .IgnoreFailure();
        }
    }
}


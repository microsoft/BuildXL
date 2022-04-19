// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
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
                    _process.Exited += () => OnExited(context, "ProcessExited");
                    return Result.Success(_process.Id);
                },
                traceOperationStarted: true,
                extraStartMessage: $"ServiceId={ServiceId}",
                messageFactory: r => $"ProcessId={r.GetValueOrDefault(defaultValue: -1)}, ServiceId={ServiceId}"
            );
        }

        /// <summary>
        /// Stops the managed process.
        /// </summary>
        /// <remarks>
        /// The method tries to stop the child process gracefully for <paramref name="gracefulShutdownTimeout"/> interval,
        /// and if the child process does not respect the shutdown request, the <see cref="Kill"/> method is called to terminate
        /// the process ungracefully.
        /// And if the termination is not done within <paramref name="killTimeout"/> the operation fails with <see cref="TimeoutException"/>.
        /// </remarks>
        public Task<Result<int>> StopAsync(OperationContext context, TimeSpan gracefulShutdownTimeout, TimeSpan killTimeout)
        {
            // To avoid race conditions with this code we separate the graceful timeout from the overall timeout.
            // The Stop method uses 'lifetime manager' to notify the child process that it should exit.
            // And if the signal is not respected, after the graceful shutdown interval the Kill method is called.
            // And if the process does not exit in a given timeout after that, the method exits with timeout.
            var totalTimeout = gracefulShutdownTimeout + killTimeout;

            bool alreadyExited = false;
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async _ =>
                {
                    if (HasExited)
                    {
                        alreadyExited = true;
                        return Result.Success(_process.ExitCode);
                    }

                    // Not using the context passed to this method, because we managed timeout here manually.
                    using var timeoutCts = new CancellationTokenSource(gracefulShutdownTimeout);
                    using var nestedContext = context.WithCancellationToken(timeoutCts.Token);
                    
                    // Terminating the process after timeout if it won't shutdown gracefully.
                    using var registration = nestedContext.Context.Token.Register(
                        () =>
                        {
                            // It is important to pass 'context' and not 'nestedContext',
                            // because 'nestedContext' will be canceled at a time we call Kill method.
                            Kill(context);
                        });

                    // Trying to shut down the service gracefully and ignoring the error, because its already being traced).
                    await _lifetimeManager.GracefulShutdownServiceAsync(nestedContext, ServiceId).IgnoreFailure();
                    EnsureProcessExitSourceIsSetOnProcessExit(context.WithoutCancellationToken(), totalTimeout);

                    // Waiting for the process exit task. It should be set to completion either by a graceful shutdown,
                    // or by calling Kill method.
                    return await _processExitSource.Task;
                },
                timeout: totalTimeout,
                extraStartMessage: $"ProcessId={ProcessId}, ServiceId={ServiceId}",
                extraEndMessage: r => $"ProcessId={ProcessId}, ServiceId={ServiceId}, ExitTime={_process.ExitTime}, ExitCode={r.GetValueOrDefault(-1)}, AlreadyExited={alreadyExited}");
        }

        private void EnsureProcessExitSourceIsSetOnProcessExit(OperationContext context, TimeSpan timeout)
        {
            // In some cases the Exit even on a process is not called and the shutdown gets stuck.
            // Creating a long-running task that will wait on process exit.
            Task.Factory.StartNew(
                () =>
                {
                    bool exitSuccessfully = _process.WaitForExit(timeout);
                    if (exitSuccessfully)
                    {
                        OnExited(context, "WaitedForExit");
                    }
                },
                TaskCreationOptions.LongRunning)
                .FireAndForget(context);
        }

        private void Kill(OperationContext context)
        {
            var result = context
                .WithoutCancellationToken() // Not using the cancellation token from the context.
                .PerformOperation(
                    Tracer,
                    () =>
                    {
                        // Using Result<string> for tracing purposes.
                        if (HasExited)
                        {
                            OnExited(context, "TerminateServiceAlreadyExited");
                            return Result.Success(_process.ExitCode).WithSuccessDiagnostics("AlreadyExited");
                        }

                        _process.Kill(context);
                        return Result.Success(_process.ExitCode).WithSuccessDiagnostics("ProcessKilled");

                    },
                    extraStartMessage: $"ProcessId={ProcessId}, ServiceId={ServiceId}",
                    messageFactory: r => $"ProcessId={ProcessId}, ServiceId={ServiceId}, {(r.Succeeded ? r.Value : r.ToString())}");

            // The _process.Kill operation may fail with an exception,
            // so we track if it was successful or not, to propagate the result back properly.
            if (result.Succeeded)
            {
                // Intentionally trying to set the result that indicates the cancellation after PerformOperation call that will never throw.
                _processExitSource.TrySetResult(result.Value);
            }
            else
            {
                _processExitSource.TrySetException(new ResultPropagationException(result));
            }
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


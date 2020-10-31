// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;
using System.Runtime.ExceptionServices;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Environment initialization for gRPC Core
    /// </summary>
    /// <remarks>
    /// gRPC has a lot of peculiarities with respect to environment initialization:
    ///  1. The environment must be initialized before any call that uses the gRPC mechanisms.
    ///  2. If gRPC mechanisms are used in parallel as we are initializing the environment, things WILL fail with weird
    ///     violation exceptions and general FFI pain ensues.
    ///  3. Some options in the gRPC environment can be re-set after we initalized and started using the environment,
    ///     some can't. At different points of the program we have or do not have information as to what should be
    ///     set to which values, so we allow for both things to happen with some restraint.
    ///  4. Attempting to set an option that can't be set after initialization will cause exceptions on the gRPC C#
    ///     wrapper layer.
    /// </remarks>
    public static class GrpcEnvironment
    {
        private static readonly Tracer Tracer = new Tracer(nameof(GrpcEnvironment));

        /// <summary>
        /// This is a fixed Guid that is used to report all logs from gRPC. It is meant to quickly search logs for gRPC
        /// operations. We also use Component and Operation, but those fields may not be available in all logs we have
        /// to look at. This guarantees we always have a deterministic way to search in such cases.
        /// </summary>
        private static readonly Guid GrpcGuid = Guid.Parse("62447f16-eea2-411c-af0d-c5a2e6661da3");

        /// <nodoc />
        public const string LocalHost = "localhost";

        private static bool IsInitialized;

        private static readonly object InitializationLock = new object();

        /// <summary>
        /// Used to ensure all gRPC consumers wait for the gRPC initialization to finish before using gRPC.
        /// </summary>
        private static readonly TaskSourceSlim<int> InitializationCompletionSource = TaskSourceSlim.Create<int>();

        /// <summary>
        /// Maximum amount of time that a consumer is willing to wait before it considers initialization doomed
        /// </summary>
        private static readonly TimeSpan GrpcInitializationWaitTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Default configuration options applied to all gRPC connections regardless of whether they are clients or
        /// servers. Specific configuration may be enabled on a per-channel basis depending on the calls to the option
        /// generators (<see cref="GetClientOptions"/> and <see cref="GetServerOptions"/>).
        ///
        /// For the most up-to-date information on gRPC options, you can look at:
        /// https://github.com/grpc/grpc/blob/master/include/grpc/impl/codegen/grpc_types.h#L142
        ///
        /// In order to know defaults, you'll need to clone the repository and search manually for wherever it is set.
        /// </summary>
        private static readonly IReadOnlyList<ChannelOption> DefaultConfiguration = new List<ChannelOption>() {
            // We don't use OpenCensus tracing.
            new ChannelOption("grpc.census", 0),

            // We don't use load balancing, much less load reporting.
            new ChannelOption("grpc.loadreporting", 0),

            // Channelz is not supported in the C# implementation for gRPC core. Even when it is implemented, support
            // for it is plain bad, so it's not much use to have it on.
            new ChannelOption("grpc.enable_channelz", 0),

            // This isn't actually supported in C#.
            new ChannelOption("grpc.enable_http_proxy", 0),

            // We don't do health checking via gRPC calls.
            new ChannelOption("grpc.inhibit_health_checking", 1),

            // Maximum message length that the channel can send. Int valued, bytes. -1 means unlimited.
            new ChannelOption(ChannelOptions.MaxSendMessageLength, -1),

            // Maximum message length that the channel can receive. Int valued, bytes. -1 means unlimited.
            new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1),

            /*
             *  String defining the optimization target for a channel.
             *  Can be: "latency"    - attempt to minimize latency at the cost of throughput
             *          "blend"      - try to balance latency and throughput
             *          "throughput" - attempt to maximize throughput at the expense of
             *                          latency
             *  Defaults to "blend". In the current implementation "blend" is equivalent to
             *  "latency".
             */
            new ChannelOption("grpc.optimization_target", "blend"),

            /* If set to zero, disables retry behavior. Otherwise, transparent retries
             * are enabled for all RPCs, and configurable retries are enabled when they
             * are configured via the service config. For details, see:
             * https://github.com/grpc/proposal/blob/master/A6-client-retries.md
             * 
             * Defaults to 1
             */
            new ChannelOption("grpc.enable_retries", 1),
        };

        /// <nodoc />
        public static IEnumerable<ChannelOption> GetClientOptions(GrpcCoreClientOptions? options = null)
        {
            return DefaultConfiguration.Concat(options?.IntoChannelOptions() ?? Enumerable.Empty<ChannelOption>());
        }

        /// <nodoc />
        public static IEnumerable<ChannelOption> GetServerOptions(GrpcCoreServerOptions? options = null)
        {
            return DefaultConfiguration.Concat(options?.IntoChannelOptions() ?? Enumerable.Empty<ChannelOption>());
        }

        /// <summary>
        /// Initialize the GRPC environment if not yet initialized.
        /// </summary>
        /// <remarks>
        /// This is here for backwards compatibility. Should be removed when/if clients migrate.
        /// </remarks>
        public static void InitializeIfNeeded(int numThreads = 70, bool handlerInliningEnabled = true)
        {
            // We mimick the old initialization logic almost completely faithfully here. The only change now is that we
            // may throw if a previous initialization failed. All clients that call this method do so before giving us
            // control, so this code below should never ever throw anyways.
            if (handlerInliningEnabled)
            {
                // Explicitly set ThreadPoolSize and CompletionQueueCount. This is what QuickBuild, drop, etc were
                // doing thus far.
                Initialize(
                    logger: null,
                    options: new GrpcEnvironmentOptions()
                    {
                        ThreadPoolSize = numThreads,
                        CompletionQueueCount = numThreads,
                        HandlerInlining = true,
                    });
            }
            else
            {
                // Explicitly do NOT change ThreadPoolSize and CompletionQueueCount, and leave them as defaults. This
                // is important to reproduce because it's what BuildXL is doing.
                Initialize(
                    logger: null,
                    options: new GrpcEnvironmentOptions()
                    {
                        ThreadPoolSize = null,
                        CompletionQueueCount = null,
                        HandlerInlining = false,
                    });
            }
        }

        /// <summary>
        /// This method should be called before every entry-point gRPC channel creation, for both server and
        /// client-side components.
        ///
        /// It makes clients wait until gRPC environment initialization has completed, timing out at
        /// <see cref="GrpcInitializationWaitTimeout"/>.
        /// </summary>
        public static void WaitUntilInitialized()
        {
            bool initializedInTime;
            try
            {
                initializedInTime = InitializationCompletionSource.Task.Wait(GrpcInitializationWaitTimeout);
            }
            catch (AggregateException exception)
            {
                var innerException = exception?.InnerException;
                Contract.AssertNotNull(innerException);
                throw new GrpcInitializationException($"gRPC environment initialization failed while waiting", innerException);
            }

            if (!initializedInTime)
            {
                throw new GrpcInitializationException($"gRPC environment initialization did not complete by timeout `{GrpcInitializationWaitTimeout}`");
            }
        }

        /// <summary>
        /// Initialize the gRPC environment
        /// </summary>
        public static void Initialize(ILogger? logger = null, GrpcEnvironmentOptions? options = null, bool overwriteSafeOptions = false)
        {
            var tracingContext = new Context(GrpcGuid, logger ?? NullLogger.Instance);
            var context = new OperationContext(tracingContext);
            options ??= new GrpcEnvironmentOptions();

            context.PerformOperation(Tracer, () =>
            {
                try
                {
                    InitializeCore(context, logger, options, overwriteSafeOptions);
                }
                catch (Exception exception)
                {
                    throw new GrpcInitializationException($"gRPC environment initialization failed", exception);
                }

                return BoolResult.Success;
            }).ThrowIfFailure();
        }

        private static void InitializeCore(OperationContext context, ILogger? logger, GrpcEnvironmentOptions options, bool overwriteSafeOptions)
        {
            lock (InitializationLock)
            {
                if (InitializationCompletionSource.Task.IsFaulted)
                {
                    AggregateException? exception = InitializationCompletionSource.Task.Exception;
                    var innerException = exception?.InnerException;
                    Contract.AssertNotNull(innerException);
                    ExceptionDispatchInfo.Capture(innerException).Throw();
                }

                if (IsInitialized)
                {
                    Tracer.Info(context, "Attempt to initialize gRPC environment aborted due to a previous initialization.");
                    if (overwriteSafeOptions)
                    {
                        InitializeSafeGrpcOptions(context, options, logger);
                    }
                }
                else
                {
                    // We don't retry if this errors because it likely means that something broke in gRPC core
                    try
                    {
                        InitializeSafeGrpcOptions(context, options, logger);
                        InitializeUnsafeGrpcOptions(context, options);
                    }
                    catch (Exception e)
                    {
                        InitializationCompletionSource.SetException(e);
                        throw;
                    }

                    IsInitialized = true;
                    InitializationCompletionSource.SetResult(1);
                }
            }
        }

        private static void InitializeSafeGrpcOptions(OperationContext context, GrpcEnvironmentOptions options, ILogger? logger)
        {
            Contract.Requires(options.LoggingVerbosity == GrpcEnvironmentOptions.GrpcVerbosity.Disabled || logger != null, "Attempt to enable gRPC internal logging without a logger to forward logs to");

            // Setting GRPC_DNS_RESOLVER=native to bypass ares DNS resolver which seems to cause
            // temporary long delays (2 minutes) while failing to resolve DNS using ares in some environments
            Environment.SetEnvironmentVariable("GRPC_DNS_RESOLVER", "native");

            if (options.ExperimentalDisableFlowControl ?? false)
            {
                Tracer.Info(context, "Disabling gRPC flow control");
                Environment.SetEnvironmentVariable("GRPC_EXPERIMENTAL_DISABLE_FLOW_CONTROL", "1");
            }

            if (options.ClientChannelBackupPollIntervalMs != null && options.ClientChannelBackupPollIntervalMs >= 0)
            {
                var value = options.ClientChannelBackupPollIntervalMs.Value.ToString();
                Tracer.Info(context, $"Setting gRPC ClientChannelBackupPollIntervalMs to `{value}`");
                Environment.SetEnvironmentVariable("GRPC_CLIENT_CHANNEL_BACKUP_POLL_INTERVAL_MS", value);
            }

            if (options.LoggingVerbosity != GrpcEnvironmentOptions.GrpcVerbosity.Disabled)
            {
                var verbosityValue = options.LoggingVerbosity.ToString("g").ToUpper();
                string traceValue = "<EMPTY>";
                Environment.SetEnvironmentVariable("GRPC_VERBOSITY", verbosityValue);

                if (options.Trace != null)
                {
                    traceValue = string.Join(",", options.Trace);
                    Environment.SetEnvironmentVariable("GRPC_TRACE", traceValue);
                }

                Tracer.Info(context, $"Initializing gRPC logger to verbosity `{verbosityValue}` with traces: {traceValue}");
                global::Grpc.Core.GrpcEnvironment.SetLogger(new GrpcLoggerAdapter(context.TracingContext));
            }
        }

        private static void InitializeUnsafeGrpcOptions(OperationContext context, GrpcEnvironmentOptions options)
        {
            if (options.ThreadPoolSize != null && options.ThreadPoolSize > 0)
            {
                Tracer.Info(context, $"Setting gRPC ThreadPoolSize to `{options.ThreadPoolSize.Value}`");
                global::Grpc.Core.GrpcEnvironment.SetThreadPoolSize(options.ThreadPoolSize.Value);
            }

            if (options.CompletionQueueCount != null && options.CompletionQueueCount > 0)
            {
                Tracer.Info(context, $"Setting gRPC CompletionQueueCount to `{options.CompletionQueueCount.Value}`");
                global::Grpc.Core.GrpcEnvironment.SetCompletionQueueCount(options.CompletionQueueCount.Value);
            }

            if (options.HandlerInlining ?? false)
            {
                Tracer.Info(context, $"Setting gRPC HandlerInlining to `{options.HandlerInlining.Value}`");
                global::Grpc.Core.GrpcEnvironment.SetHandlerInlining(options.HandlerInlining.Value);
            }
        }

        /// <summary>
        /// Exception type for errors from initializing the gRPC Core environment.
        /// </summary>
        public class GrpcInitializationException : Exception
        {
            /// <inheritdoc />
            public GrpcInitializationException(string message)
                : base(message) { }

            /// <inheritdoc />
            public GrpcInitializationException(string message, Exception inner)
                : base(message, inner) { }
        }
    }
}

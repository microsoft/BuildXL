// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.ConfigurationHelpers;
using Grpc.Core;

#if NET6_0_OR_GREATER
using System.Net;
using Grpc.Net.Client;
#endif

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Channel creation options used by <see cref="GrpcChannelFactory"/>.
    /// </summary>
    public record ChannelCreationOptions(
        string Host,
        int GrpcPort,

        // Only one of those properties must be non-null.
        IEnumerable<ChannelOption>? GrpcCoreOptions, // Applicable only for Grcp.Core version.
        GrpcDotNetClientOptions? GrpcDotNetOptions // Applicable for Grpc.Net version.
        )
    {
        // Have to use an explicit property becuase MemberNotNullWhen attribute can't be applied to a "property" declared with primary constructors.
        [MemberNotNullWhen(true, nameof(EncryptionOptions))]
        public bool EncryptionEnabled { get; init; }

        public ChannelEncryptionOptions? EncryptionOptions { get; init; } // Null if encryption if disabled
    }

    /// <summary>
    /// A helper factory for creating grpc channels (Grpc.Core or Grpc.Net once) based on configuration.
    /// </summary>
    public static class GrpcChannelFactory
    {
        private static Tracer Tracer { get; } = new Tracer(nameof(GrpcChannelFactory));

        public static ChannelBase CreateChannel(OperationContext context, ChannelCreationOptions channelOptions, string channelType)
        {
            Tracer.Info(context, $"Grpc Encryption Enabled: {channelOptions.EncryptionEnabled}, GRPC Host: {channelOptions.Host}, GRPC Port: {channelOptions.GrpcPort}, ChannelType: {channelType}.");
            return CreateGrpcChannel(context, channelOptions);
        }

#if NET6_0_OR_GREATER

        private static ChannelBase CreateGrpcChannel(OperationContext context, ChannelCreationOptions channelOptions)
        {
            var grpcDotNetSpecificOptions = channelOptions.GrpcDotNetOptions ?? GrpcDotNetClientOptions.Default;
            var handler = new SocketsHttpHandler
            {
                UseCookies = false,
                Expect100ContinueTimeout = TimeSpan.Zero,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                EnableMultipleHttp2Connections = true,
            };

            grpcDotNetSpecificOptions.ConnectionTimeout.ApplyIfNotNull(v => handler.ConnectTimeout = v);
            grpcDotNetSpecificOptions.PooledConnectionLifetime.ApplyIfNotNull(v => handler.PooledConnectionIdleTimeout = v);
            grpcDotNetSpecificOptions.PooledConnectionLifetime.ApplyIfNotNull(v => handler.PooledConnectionLifetime = v);

            if (grpcDotNetSpecificOptions.KeepAliveEnabled)
            {
                handler.KeepAlivePingDelay = grpcDotNetSpecificOptions.KeepAlivePingDelay;
                handler.KeepAlivePingTimeout = grpcDotNetSpecificOptions.KeepAlivePingTimeout;
                handler.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always;
            }

            if (!string.IsNullOrEmpty(grpcDotNetSpecificOptions.DecompressionMethods)
                && Enum.TryParse<DecompressionMethods>(grpcDotNetSpecificOptions.DecompressionMethods, out var decompressionMethods))
            {
                handler.AutomaticDecompression = decompressionMethods;
            }

            var options = new GrpcChannelOptions
            {
                MaxSendMessageSize = grpcDotNetSpecificOptions.MaxSendMessageSize,
                MaxReceiveMessageSize = grpcDotNetSpecificOptions.MaxReceiveMessageSize,
                HttpHandler = handler,
                DisposeHttpClient = true,
            };

            string grpcMinVerbosity = grpcDotNetSpecificOptions.MinLogLevelVerbosity != null
                ? ((Microsoft.Extensions.Logging.LogLevel)grpcDotNetSpecificOptions.MinLogLevelVerbosity).ToString()
                : "disabled";
            Tracer.Debug(context, $"Grpc.Net logging min verbosity: {grpcMinVerbosity}");

            if (grpcDotNetSpecificOptions.MinLogLevelVerbosity != null)
            {
                options.LoggerFactory = new GrpcCacheLoggerAdapter(Tracer, context.TracingContext.CreateNested(Tracer.Name), (Microsoft.Extensions.Logging.LogLevel)grpcDotNetSpecificOptions.MinLogLevelVerbosity.Value);
            }

            string httpOrHttps = channelOptions.EncryptionEnabled ? "https" : "http";
            string address = $"{httpOrHttps}://{channelOptions.Host}:{channelOptions.GrpcPort}";

            bool encryptionSetupCompleted = false;
            if (channelOptions.EncryptionEnabled)
            {
                // CertificateSubjectName, CertificateChainPath, IdentityTokenLocation
                var setupResult = SetupChannelOptionsForEncryption(context, options, channelOptions.EncryptionOptions, handler);
                if (setupResult)
                {
                    // The error is already traced.
                    encryptionSetupCompleted = true;
                    Tracer.Info(context, "Grpc.NET auth is enabled.");
                }
            }

            if (!encryptionSetupCompleted)
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            }

            return GrpcChannel.ForAddress(address, options);
        }

        private static BoolResult SetupChannelOptionsForEncryption(OperationContext context, GrpcChannelOptions options, ChannelEncryptionOptions encryptionOptions, SocketsHttpHandler httpHandler)
        {
            var (certificateSubjectName, certificateChainsPath, identityTokenPath, storeLocation) = encryptionOptions;
            X509Certificate2? certificate = null;
            string error = string.Empty;

            return context.PerformOperation(
                Tracer,
                () =>
                {
                    certificate = GrpcEncryptionUtils.TryGetEncryptionCertificate(certificateSubjectName, storeLocation, out error);

                    if (certificate == null)
                    {
                        return new BoolResult($"No certificate found that matches subject name: '{certificateSubjectName}'. Error={error}");
                    }

                    httpHandler.SslOptions.ClientCertificates = new X509CertificateCollection { certificate };

                    httpHandler.SslOptions.RemoteCertificateValidationCallback =
                        (requestMessage, certificate, chain, errors) =>
                        {
                            if (certificateChainsPath != null)
                            {
                                if (!GrpcEncryptionUtils.TryValidateCertificate(certificateChainsPath, chain, out string errorMessage))
                                {
                                    Tracer.Warning(context, $"Certificate is not validated: '{errorMessage}'.");
                                    return false;
                                }
                            }

                            // If the path for the chains is not provided, we will not validate the certificate.
                            return true;
                        };

                    if (string.IsNullOrEmpty(identityTokenPath))
                    {
                        return new BoolResult("Identity token path is not set.");
                    }

                    var credentials = GetCallCredentialsWithToken(identityTokenPath);

                    if (credentials is null)
                    {
                        return new BoolResult($"Can't obtain build identity token from identity token path '{identityTokenPath}'.");
                    }

                    options.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials);
                    return BoolResult.Success;
                });
        }

        private static CallCredentials? GetCallCredentialsWithToken(string buildIdentityTokenPath)
        {
            string? token = GrpcEncryptionUtils.TryGetTokenBuildIdentityToken(buildIdentityTokenPath);

            if (token == null)
            {
                // The error will be traced outside of this method.
                return null;
            }

            return CallCredentials.FromInterceptor((context, metadata) =>
            {
                if (!string.IsNullOrEmpty(token))
                {
                    metadata.Add("Authorization", token);
                }

                return Task.CompletedTask;
            });
        }

#else

        private static ChannelBase CreateGrpcChannel(OperationContext context, ChannelCreationOptions channelOptions)
        {
            GrpcEnvironment.WaitUntilInitialized();

            var channelCreds = ChannelCredentials.Insecure;

            var options = (channelOptions.GrpcCoreOptions ?? Array.Empty<ChannelOption>()).ToList();

            if (channelOptions.EncryptionEnabled)
            {
                try
                {
                    channelCreds = TryGetSecureChannelCredentials(context, channelOptions.EncryptionOptions.CertificateSubjectName, channelOptions.EncryptionOptions.StoreLocation, out var hostName) ?? ChannelCredentials.Insecure;
                    if (channelCreds != ChannelCredentials.Insecure)
                    {
                        options.Add(new ChannelOption(ChannelOptions.SslTargetNameOverride, hostName));
                    }
                }
                catch (Exception ex)
                {
                    Tracer.Error(context, ex, $"Creating Encrypted Grpc Channel Failed.");
                }
            }

            Tracer.Debug(context, $"Client connecting to {channelOptions.Host}:{channelOptions.GrpcPort}. Channel Encrypted: {channelCreds != ChannelCredentials.Insecure}");

            var channel = new Channel(channelOptions.Host, channelOptions.GrpcPort, channelCreds, options: options);
            return channel;
        }

        /// <summary>
        /// Create and return SSL credentials from user certificate, else null.
        /// </summary>
        private static ChannelCredentials? TryGetSecureChannelCredentials(OperationContext context, string certificateName, StoreLocation storeLocation, out string? hostName)
        {
            var keyCertPairResult = GrpcEncryptionUtils.TryGetSecureChannelCredentials(certificateName, storeLocation, out hostName);

            if (keyCertPairResult.Succeeded)
            {
                Tracer.Debug(context, $"Found Grpc Encryption Certificate. ");
                return new SslCredentials(keyCertPairResult.Value.CertificateChain);
            }

            Tracer.Warning(context, $"Failed to get GRPC SSL Credentials: {keyCertPairResult}");
            return null;
        }
#endif

        public static async Task ConnectAsync(this ChannelBase channel, string host, int port, IClock clock, TimeSpan timeout)
        {
            // We have observed cases in production where a GrpcCopyClient instance consistently fails to perform
            // copies against the destination machine. We are suspicious that no connection is actually being
            // established. This is meant to ensure that we don't perform copies against uninitialized channels.
            try
            {
                if (channel is Channel channelCore)
                {
                    if (timeout == Timeout.InfiniteTimeSpan || timeout <= TimeSpan.Zero)
                    {
                        await channelCore.ConnectAsync();
                        return;
                    }
                    else
                    {
                        var deadline = clock.UtcNow + timeout;
                        await channelCore.ConnectAsync(deadline);
                        return;
                    }
                }
#if NET6_0_OR_GREATER
                else if (channel is GrpcChannel channelDotNet)
                {
                    // Grpc.Net version of ConnectAsync takes a cancellation token and not a deadline.
                    if (timeout == Timeout.InfiniteTimeSpan || timeout <= TimeSpan.Zero)
                    {
                        await channelDotNet.ConnectAsync();
                        return;
                    }
                    else
                    {
                        using var cts = new CancellationTokenSource(timeout);
                        await channelDotNet.ConnectAsync(cts.Token);
                    }

                    return;
                }
#endif
            }
            catch (TaskCanceledException)
            {
                // If deadline occurs, ConnectAsync fails with TaskCanceledException.
                // Wrapping it into TimeoutException instead.
                throw new GrpcConnectionTimeoutException($"Failed to connect to {host}:{port} at {timeout}.");
            }

            throw Contract.AssertFailure($"Unknown channel type: {channel?.GetType()}");
        }

        public static async Task DisconnectAsync(this ChannelBase channel)
        {
            // GrpcChannelFactory.CreateChannel returns a ChannelBase, which is an abstract class. The actual type is
            // either a Channel (gRPC Core) or a GrpcChannel (gRPC.NET). We need to dispose the channel in order to
            // release resources.
            // ChannelBase exposes a ShutdownAsync method, which is successfully implemented by gRPC Core's Channel,
            // but not by Grpc.NET's GrpcChannel. Grpc.NET's GrpcChannel implements IDisposable instead.
            if (channel is Channel channelCore)
            {
                await channelCore.ShutdownAsync();
                return;
            }
#if NET6_0_OR_GREATER
            else if (channel is GrpcChannel channelDotNet)
            {
                await Task.Yield();
                var disposeTask = Task.Run(() => channelDotNet.Dispose());
                await disposeTask;
                return;
            }
#endif

            throw Contract.AssertFailure($"Unknown channel type: {channel?.GetType()}");
        }
    }
}

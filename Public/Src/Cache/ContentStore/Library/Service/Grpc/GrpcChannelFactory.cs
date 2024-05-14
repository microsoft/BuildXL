// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
using Grpc.Core;
using BuildXL.Cache.ContentStore.Distributed;

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
        MachineLocation Location,
        IEnumerable<ChannelOption>? GrpcCoreOptions,
        GrpcDotNetClientOptions? GrpcDotNetOptions
        );

    /// <summary>
    /// A helper factory for creating gRPC channels (gRPC.Core or gRPC.NET)
    /// </summary>
    public static class GrpcChannelFactory
    {
        private static Tracer Tracer { get; } = new Tracer(nameof(GrpcChannelFactory));

        public static ChannelBase CreateChannel(OperationContext context, ChannelCreationOptions channelOptions, string channelType)
        {
            Tracer.Info(context, $"Location: {channelOptions.Location}. ChannelType: {channelType}.");
            return CreateGrpcChannel(context, channelOptions);
        }

#if NET6_0_OR_GREATER

        /// <summary>
        /// The <see cref="SocketsHttpHandler"/> is used by the <see cref="HttpClient"/>, which is shared by all
        /// <see cref="GrpcChannel"/>. created by <see cref="CreateChannel(OperationContext, ChannelCreationOptions, string)"/>.
        ///
        /// It is shared across all channels, because inside it contains a connection pool. Using separate handlers for
        /// each channel creates problems because the connection pool is not shared, and therefore an outsized number
        /// of connections are created, which causes socket exhaustion.
        /// 
        /// See: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines
        /// </summary>
        private readonly static SocketsHttpHandler SocketsHttpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,

            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 100,

            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),

            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromMinutes(1),
        };

        private static bool HttpsInterceptComplete = false;

        private static void InterceptHttpsValidation(OperationContext context, SocketsHttpHandler httpHandler)
        {
            if (HttpsInterceptComplete)
            {
                return;
            }

            lock (SocketsHttpHandler)
            {
                if (HttpsInterceptComplete)
                {
                    return;
                }

                context.PerformOperation(
                    Tracer,
                    () =>
                    {
                        var (certificateSubjectName, certificateChainsPath, identityTokenPath) = GrpcEncryptionUtils.GetChannelEncryptionOptions();
                        X509Certificate2? certificate = null;
                        string error = string.Empty;

                        certificate = GrpcEncryptionUtils.TryGetEncryptionCertificate(certificateSubjectName, out error);
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
                                        Tracer.Error(context, $"Certificate is not validated: '{errorMessage}'.");
                                        return false;
                                    }
                                }

                                // If the path for the chains is not provided, we will not validate the certificate.
                                return true;
                            };


                        return BoolResult.Success;
                    }).IgnoreFailure();

                HttpsInterceptComplete = true;
            }
        }

        private static ChannelBase CreateGrpcChannel(OperationContext context, ChannelCreationOptions channelOptions)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            var grpcDotNetSpecificOptions = channelOptions.GrpcDotNetOptions ?? GrpcDotNetClientOptions.Default;

            var options = new GrpcChannelOptions
            {
                // The options below are important to ensure that requests don't fail when they have large payloads.
                // This happens often during copies in the datacenter.
                MaxSendMessageSize = int.MaxValue,
                MaxReceiveMessageSize = int.MaxValue,

                // The options below are important to prevent port exhaustion by sharing a single connection pool.
                HttpHandler = SocketsHttpHandler,
                DisposeHttpClient = false,
            };

            var target = channelOptions.Location.ToGrpcHost();
            if (target.Encrypted)
            {
                var channelEncryptionOptions = GrpcEncryptionUtils.GetChannelEncryptionOptions();

                if (string.IsNullOrEmpty(channelEncryptionOptions.IdentityTokenPath))
                {
                    Tracer.Info(context, $"Identity token path hasn't been set by host system. Establishing encrypted connections is unsupported.");
                }
                else
                {
                    string? token = GrpcEncryptionUtils.TryGetTokenBuildIdentityToken(channelEncryptionOptions.IdentityTokenPath);

                    if (token == null)
                    {
                        Tracer.Info(context, $"Can't obtain build identity token from identity token path '{channelEncryptionOptions.IdentityTokenPath}'.");
                    }
                    else
                    {
                        InterceptHttpsValidation(context, SocketsHttpHandler);
                        var credentials = CallCredentials.FromInterceptor((context, metadata) =>
                        {
                            if (!string.IsNullOrEmpty(token))
                            {
                                metadata.Add("Authorization", token);
                            }

                            return Task.CompletedTask;
                        });
                        options.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials);
                        Tracer.Info(context, $"gRPC.NET authentication enabled");
                    }
                }
            }

            return GrpcChannel.ForAddress(target.ToGrpcUri(), options);
        }

#else

        private static ChannelBase CreateGrpcChannel(OperationContext context, ChannelCreationOptions channelOptions)
        {
            GrpcEnvironment.WaitUntilInitialized();

            var channelCreds = ChannelCredentials.Insecure;

            var options = (channelOptions.GrpcCoreOptions ?? Array.Empty<ChannelOption>()).ToList();

            var info = channelOptions.Location.ToGrpcHost();
            if (info.Encrypted)
            {
                var encryptionOptions = GrpcEncryptionUtils.GetChannelEncryptionOptions();
                try
                {
                    channelCreds = TryGetSecureChannelCredentials(context, encryptionOptions.CertificateSubjectName, out var hostName) ?? ChannelCredentials.Insecure;
                    if (channelCreds != ChannelCredentials.Insecure)
                    {
                        options.Add(new ChannelOption(ChannelOptions.SslTargetNameOverride, hostName));
                    }
                }
                catch (Exception ex)
                {
                    Tracer.Info(context, ex, $"Creating encrypted gRPC channel failed. This will not fail the build, we are simply falling back to unencrypted channels.");
                }
            }

            var channel = new Channel(info.Host, info.Port, channelCreds, options: options);
            return channel;
        }

        /// <summary>
        /// Create and return SSL credentials from user certificate, else null.
        /// </summary>
        private static ChannelCredentials? TryGetSecureChannelCredentials(OperationContext context, string certificateName, out string? hostName)
        {
            var keyCertPairResult = GrpcEncryptionUtils.TryGetSecureChannelCredentials(certificateName, out hostName);

            if (keyCertPairResult.Succeeded)
            {
                Tracer.Debug(context, $"Found Grpc Encryption Certificate. ");
                return new SslCredentials(keyCertPairResult.Value.CertificateChain);
            }

            Tracer.Warning(context, $"Failed to get GRPC SSL Credentials: {keyCertPairResult}");
            return null;
        }
#endif

        public static async Task ConnectAsync(this ChannelBase channel, MachineLocation location, IClock clock, TimeSpan timeout)
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
                throw new GrpcConnectionTimeoutException($"Failed to connect to {location} at {timeout}.");
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

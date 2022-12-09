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
        bool UseGrpcDotNet,

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

        public static ChannelBase CreateChannel(OperationContext context, ChannelCreationOptions channelOptions)
        {
            Tracer.Info(context, $"Grpc Encryption Enabled: {channelOptions.EncryptionEnabled}, GRPC Port: {channelOptions.GrpcPort}, GrpcDotNet: {channelOptions.UseGrpcDotNet}.");
            if (!channelOptions.UseGrpcDotNet)
            {
                return CreateGrpcCoreChannel(context, channelOptions);
            }

            return CreateGrpcDotNetChannel(context, channelOptions);
        }

        private static ChannelBase CreateGrpcDotNetChannel(OperationContext context, ChannelCreationOptions channelOptions)
        {
            Contract.Requires(channelOptions.GrpcDotNetOptions != null);

#if NET6_0_OR_GREATER
            var grpcDotNetSpecificOptions = channelOptions.GrpcDotNetOptions;
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
#else
            throw new InvalidOperationException("Can't create Grpc.Net client on non-net6 platform.");
#endif
        }

#if NET6_0_OR_GREATER
        private static BoolResult SetupChannelOptionsForEncryption(OperationContext context, GrpcChannelOptions options, ChannelEncryptionOptions encryptionOptions, SocketsHttpHandler httpHandler)
        {
            var (certificateSubjectName, certificateChainsPath, identityTokenPath) = encryptionOptions;
            X509Certificate2? certificate = null;
            string error = string.Empty;

            return context.PerformOperation(
                Tracer,
                () =>
                {
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
#endif

        private static ChannelBase CreateGrpcCoreChannel(OperationContext context, ChannelCreationOptions channelOptions)
        {
            Contract.Requires(channelOptions.GrpcCoreOptions != null);

            var channelCreds = ChannelCredentials.Insecure;

            var options = channelOptions.GrpcCoreOptions.ToList();

            if (channelOptions.EncryptionEnabled)
            {
                try
                {
                    channelCreds = TryGetSecureChannelCredentials(context, channelOptions.EncryptionOptions.CertificateSubjectName, out var hostName) ?? ChannelCredentials.Insecure;
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

        public static async Task ConnectAsync(this ChannelBase channel, Interfaces.Time.IClock clock, TimeSpan timeout)
        {
            if (channel is Channel channelCore)
            {
                var deadline = clock.UtcNow + timeout;
                await channelCore.ConnectAsync(deadline);
                return;
            }
#if NET6_0_OR_GREATER
            else if (channel is GrpcChannel channelDotNet)
            {
                // Grpc.Net version of ConnectAsync takes a cancellation token and not a deadline.
                using var cts = new CancellationTokenSource(timeout);
                await channelDotNet.ConnectAsync(cts.Token);
                return;
            }
#endif

            throw Contract.AssertFailure($"Unknown channel type: {channel?.GetType()}");
        }
    }
}

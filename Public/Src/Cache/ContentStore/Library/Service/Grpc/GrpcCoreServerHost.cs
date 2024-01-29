// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Net;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using Grpc.Core;

#nullable enable

namespace BuildXL.Cache.ContentStore.Service.Grpc;

public record GrpcCoreServerHostConfiguration(
    int? GrpcPort = null,
    int? EncryptedGrpcPort = null,
    int RequestCallTokensPerCompletionQueue = 7000,
    GrpcCoreServerOptions? GrpcCoreServerOptions = null);

public class GrpcCoreServerHost : IGrpcServerHost<GrpcCoreServerHostConfiguration>
{
    public Tracer Tracer { get; } = new(nameof(GrpcCoreServerHost));

    private Server? _grpcServer;

    /// <inheritdoc />
    public Task<BoolResult> StartAsync(
        OperationContext context,
        GrpcCoreServerHostConfiguration configuration,
        IEnumerable<IGrpcServiceEndpoint> endpoints)
    {
        return context.PerformOperationAsync(
            Tracer,
            () =>
            {
                // This first step is actually a basic check that the gRPC environment is initialized.
                GrpcEnvironment.WaitUntilInitialized();

                GrpcCoreServerOptions? grpcCoreServerOptions = configuration.GrpcCoreServerOptions;

                _grpcServer = new Server(GrpcEnvironment.GetServerOptions(grpcCoreServerOptions))
                {
                    Ports = { },
                    RequestCallTokensPerCompletionQueue = configuration.RequestCallTokensPerCompletionQueue,
                };

                Contract.Assert(configuration.GrpcPort != configuration.EncryptedGrpcPort, "GrpcPort and EncryptedGrpcPort cannot be the same");
                if (configuration.GrpcPort is not null)
                {
                    _grpcServer.Ports.Add(new ServerPort(IPAddress.Any.ToString(), configuration.GrpcPort!.Value, ServerCredentials.Insecure));
                }

                if (configuration.EncryptedGrpcPort is not null)
                {
                    try
                    {
                        ServerCredentials? credentials = TryGetEncryptedCredentials(context);
                        if (credentials != null)
                        {
                            _grpcServer.Ports.Add(new ServerPort(IPAddress.Any.ToString(), configuration.EncryptedGrpcPort!.Value, credentials));
                            Tracer.Debug(context, $"Server creating Encrypted Grpc channel on port {configuration.EncryptedGrpcPort}");
                        }
                        else
                        {
                            Tracer.Error(context, message: "Failed to get SSL Credentials. Not creating encrypted Grpc channel.");
                            if (configuration.GrpcPort is null)
                            {
                                throw new InvalidOperationException("Failed to get SSL credentials for establishing gRPC server. There is no unencrypted port for this, so the service can't be started up.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Tracer.Error(context, ex, "Creating SSL Secured Grpc Channel Failed.");
                        if (configuration.GrpcPort is null)
                        {
                            throw;
                        }
                    }
                }

                foreach (var endpoint in endpoints)
                {
                    endpoint.BindServices(_grpcServer.Services);
                }

                _grpcServer.Start();

                return Task.FromResult(BoolResult.Success);
            });
    }

    /// <inheritdoc />
    public Task<BoolResult> StopAsync(OperationContext context, GrpcCoreServerHostConfiguration configuration)
    {
        return context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                if (_grpcServer is not null)
                {
                    await _grpcServer.KillAsync();
                }

                return BoolResult.Success;
            });
    }

    /// <summary>
    /// Returns the SSL Credentials to use for the server.
    /// </summary>
    protected virtual ServerCredentials? TryGetEncryptedCredentials(OperationContext context)
    {
        try
        {
            var encryptionOptions = GrpcEncryptionUtils.GetChannelEncryptionOptions();
            var keyCertPairResult = GrpcEncryptionUtils.TryGetSecureChannelCredentials(encryptionOptions.CertificateSubjectName, encryptionOptions.StoreLocation, out _);

            if (keyCertPairResult.Succeeded)
            {
                Tracer.Debug(context, $"Found gRPC Encryption Certificate.");
                return new SslServerCredentials(
                    new List<KeyCertificatePair> { new KeyCertificatePair(keyCertPairResult.Value.CertificateChain, keyCertPairResult.Value.PrivateKey) },
                    null,
                    SslClientCertificateRequestType.DontRequest); //Since this is an internal channel, client certificate is not requested or verified.
            }
            else
            {
                Tracer.Error(context, message: $"Failed to get gRPC SSL credentials: {keyCertPairResult}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Tracer.Error(context, ex, message: $"Failed to get gRPC SSL credentials");
            return null;
        }
    }
}


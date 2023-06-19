// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
    int GrpcPort = ContentStore.Grpc.GrpcConstants.DefaultGrpcPort,
    int EncryptedGrpcPort = ContentStore.Grpc.GrpcConstants.DefaultEncryptedGrpcPort,
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
                bool? encryptionEnabled = grpcCoreServerOptions?.EncryptionEnabled;
                Tracer.Info(
                    context,
                    $"Grpc Encryption Enabled = {encryptionEnabled == true}, Encrypted GRPC Port: {configuration.EncryptedGrpcPort}, Unencrypted GRPC Port: {configuration.GrpcPort}");

                _grpcServer = new Server(GrpcEnvironment.GetServerOptions(grpcCoreServerOptions))
                {
                    Ports = { new ServerPort(IPAddress.Any.ToString(), configuration.GrpcPort, ServerCredentials.Insecure) },
                    RequestCallTokensPerCompletionQueue = configuration.RequestCallTokensPerCompletionQueue,
                };

                if (encryptionEnabled == true)
                {
                    try
                    {
                        ServerCredentials? serverSSLCreds = TryGetEncryptedCredentials(context);
                        if (serverSSLCreds != null)
                        {
                            _grpcServer.Ports.Add(new ServerPort(IPAddress.Any.ToString(), configuration.EncryptedGrpcPort, serverSSLCreds));
                            Tracer.Debug(context, $"Server creating Encrypted Grpc channel on port {configuration.EncryptedGrpcPort}");
                        }
                        else
                        {
                            Tracer.Error(context, message: "Failed to get SSL Credentials. Not creating encrypted Grpc channel.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Tracer.Error(context, ex, "Creating SSL Secured Grpc Channel Failed.");
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
        return null;
    }
}


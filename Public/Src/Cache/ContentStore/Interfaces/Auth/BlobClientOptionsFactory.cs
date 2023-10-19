// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth;

/// <summary>
/// Centralized point for common methods pertaining to Azure Storage authentication.
/// </summary>
internal static class BlobClientOptionsFactory
{
    private static readonly HttpClientTransport Transport = CreateTransport();

    private static HttpClientTransport CreateTransport()
    {
        // HttpClient is meant to be reused, so we need to make sure that all clients created by the application
        // utilize the same instance. This class is the entry point to guarantee that.
        // See: https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines#recommended-use

        // We have the need to configure the amount of open connections: when using the defaults, the number of
        // connections is unbounded, and we have observed builds where there end up being tens of thousands of open
        // sockets, which can (and did) hit the per-process limit of open files, crashing the engine.
        //
        // Performance testing has been done with the below value and we have determined it basically doesn't matter
        // what this value is. The intent behind setting it low is to avoid 
        var maxConnectionsPerServer = 100;
#if NETCOREAPP2_1_OR_GREATER
        // See: https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler?view=net-7.0
        var handler = new SocketsHttpHandler()
        {
            MaxConnectionsPerServer = maxConnectionsPerServer,
            // The Azure Storage IP addresses are expected to be static, so we don't need to re-create a
            // pooled connection often to work around IP addresses changing.
            PooledConnectionLifetime = TimeSpan.FromHours(1),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5)
        };
#else
        // .NET Standard 2.0 does not have SocketsHttpHandler, so we need to use HttpClientHandler instead.
        // See: https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler?view=net-7.0
        var handler = 
            new HttpClientHandler
            {
                MaxConnectionsPerServer = maxConnectionsPerServer
            };
#endif

        var httpClient = new HttpClient(handler);

        return new HttpClientTransport(httpClient);
    }

    /// <summary>
    /// Centralized point to override options for all blob clients created by the application.
    /// </summary>
    public static BlobClientOptions CreateOrOverride(BlobClientOptions? baseline)
    {
        baseline ??= new BlobClientOptions();
        baseline.Transport = Transport;
        return baseline;
    }
}

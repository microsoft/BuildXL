// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Http;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth;

/// <summary>
/// Centralized point for common methods pertaining to Azure Storage authentication.
/// </summary>
internal static class BlobClientOptionsFactory
{
    /// <summary>
    /// We will reuse an HttpClient for the transport backing the blob clients. HttpClient is meant to be reused anyway
    /// (https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient?view=net-7.0#instancing)
    /// but crucially we have the need to configure the amount of open connections: when using the defaults,
    /// the number of connections is unbounded, and we have observed builds where there end up being tens of thousands
    /// of open sockets, which can (and did) hit the per-process limit of open files, crashing the engine.
    /// </summary>
    private static readonly HttpClient HttpClient = new(
        new HttpClientHandler
        {
            // If left unbounded, we have observed spikes of >65k open sockets (at which point we hit
            // the OS limit of open files for the process - on Linux, where sockets count as files).
            // Running builds where we limit this value all the way down to 100 didn't see
            // any noticeable performance impact, so 30k shouldn't pose a problem.
            // The configurable limit is per-client and per-server, but because we will reuse this HttpClient
            // for all BlobClients, we are effectively limiting the number of open connections in general.
            MaxConnectionsPerServer = 30_000
        });

    private static readonly HttpClientTransport Transport = new(HttpClient);

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

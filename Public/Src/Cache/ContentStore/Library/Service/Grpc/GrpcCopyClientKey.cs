// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Grpc;

namespace BuildXL.Cache.ContentStore.Service.Grpc;

/// <summary>
/// Key to lookup <see cref="GrpcCopyClient"/> in <see cref="GrpcCopyClientCache"/>.
/// </summary>
public readonly struct GrpcCopyClientKey : IEquatable<GrpcCopyClientKey>
{
    /// <nodoc />
    public string Host { get; }

    /// <nodoc />
    public int GrpcPort { get; }

    /// <nodoc />
    public MachineLocation Location { get; }

    /// <nodoc />
    public GrpcCopyClientKey(MachineLocation location)
    {
        Location = location;

        var (host, grpcPort) = location.ExtractHostPort();
        Host = host;
        GrpcPort = grpcPort ?? GrpcConstants.DefaultGrpcPort;
    }

    /// <nodoc />
    public bool Equals([AllowNull] GrpcCopyClientKey other)
    {
        return string.Equals(Host, other.Host, StringComparison.InvariantCultureIgnoreCase)
               && GrpcPort == other.GrpcPort;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is GrpcCopyClientKey key && Equals(key);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return (Host, GrpcPort).GetHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"({Host}, {GrpcPort})";
    }
}

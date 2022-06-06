// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Location information for a machine usually represented as UNC path with machine name and a root path.
    /// </summary>
    public readonly struct MachineLocation : IEquatable<MachineLocation>, IEquatable<string>, IEquatable<byte[]>
    {
        public const string GrpcUriSchemePrefix = "grpc://";

        /// <summary>
        /// Binary representation of a machine location.
        /// </summary>
        public byte[] Data { get; init; }

        /// <summary>
        /// Gets whether the current machine location represents valid data
        /// </summary>
        [JsonIgnore]
        public bool IsValid => Data != null;

        /// <summary>
        /// Gets the path representation of the machine location
        /// </summary>
        public string Path { get; init; }

        /// <nodoc />
        public MachineLocation(string data)
        {
            Contract.Requires(data != null);

            Data = Encoding.UTF8.GetBytes(data);
            Path = data;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Path;
        }

        /// <inheritdoc />
        public bool Equals(MachineLocation other)
        {
            return ByteArrayComparer.ArraysEqual(Data, other.Data);
        }

        /// <inheritdoc />
        public bool Equals(string other)
        {
            if (Path is null)
            {
                return other is null;
            }

            return Path.Equals(other);
        }

        /// <inheritdoc />
        public bool Equals(byte[] other)
        {
            return ByteArrayComparer.ArraysEqual(Data, other);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            return (obj is MachineLocation location && Equals(location))
                || (obj is string str && Equals(str))
                || (obj is byte[] arr && Equals(arr));
        }

        /// <nodoc />
        public static bool operator ==(MachineLocation lhs, MachineLocation rhs)
        {
            return lhs.Equals(rhs);
        }

        /// <nodoc />
        public static bool operator !=(MachineLocation lhs, MachineLocation rhs)
        {
            return !lhs.Equals(rhs);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // GetHashCode is null-safe
            return ByteArrayComparer.Instance.GetHashCode(Data);
        }

        public static MachineLocation Create(string machineName, int port)
        {
            return new MachineLocation($"{MachineLocation.GrpcUriSchemePrefix}{machineName}:{port}/");
        }

        public (string host, int? port) ExtractHostInfo()
        {
            if (Path.StartsWith(GrpcUriSchemePrefix))
            {
                // This is a uri format machine location
                var uri = new Uri(Path);
                return (uri.Host, uri.Port);
            }

            var sourcePath = new AbsolutePath(Path);

            // TODO: Keep the segments in the AbsolutePath object?
            // TODO: Indexable structure?
            var segments = sourcePath.GetSegments();
            Contract.Assert(segments.Count >= 1);

            string host = GetHostName(sourcePath.IsLocal, segments);

            return (host, null);
        }

        /// <summary>
        /// Extract the host name from an AbsolutePath's segments.
        /// </summary>
        public static string GetHostName(bool isLocal, IReadOnlyList<string> segments)
        {
            if (OperatingSystemHelper.IsWindowsOS)
            {
                return isLocal ? "localhost" : segments.First();
            }
            else
            {
                // Linux always uses the first segment as the host name.
                return segments.First();
            }
        }
    }
}

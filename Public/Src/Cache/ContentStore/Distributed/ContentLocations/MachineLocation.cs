// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Location information for a machine usually represented as UNC path with machine name and a root path.
    /// </summary>
    public readonly struct MachineLocation : IEquatable<MachineLocation>
    {
        public  const string GrpcUriSchemePrefix = "grpc://";

        /// <summary>
        /// Binary representation of a machine location.
        /// </summary>
        public byte[] Data { get; }

        /// <summary>
        /// Gets whether the current machine location represents valid data
        /// </summary>
        public bool IsValid => Data != null;

        /// <summary>
        /// Gets the path representation of the machine location
        /// </summary>
        public string Path { get; }

        /// <nodoc />
        public MachineLocation(byte[] data)
        {
            Contract.Requires(data != null);

            Data = data;
            Path = Encoding.UTF8.GetString(data);
        }

        /// <nodoc />
        public MachineLocation(string data)
        {
            Contract.Requires(data != null);

            Data = Encoding.UTF8.GetBytes(data);
            Path = data;
        }

        /// <inheritdoc />
        public override string ToString() => Path;

        /// <inheritdoc />
        public bool Equals(MachineLocation other) => ByteArrayComparer.ArraysEqual(Data, other.Data);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            return obj is MachineLocation location && Equals(location);
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
            Contract.Assert(segments.Count >= 4);

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

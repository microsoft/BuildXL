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
    public readonly record struct MachineLocation : IEquatable<MachineLocation>
    {
        public const string GrpcUriSchemePrefix = "grpc://";

        /// <summary>
        /// Gets whether the current machine location represents valid data
        /// </summary>
        [JsonIgnore]
        public bool IsValid => Path != null;

        /// <summary>
        /// Gets the path representation of the machine location
        /// </summary>
        public string Path { get; init; }

        /// <nodoc />
        public MachineLocation(string path)
        {
            Contract.Requires(path != null);
            Path = path;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Path;
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

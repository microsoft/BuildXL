// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Location information for a machine usually represented as UNC path with machine name and a root path.
    /// </summary>
    public readonly record struct MachineLocation
    {
        private const string GrpcUriSchemePrefix = "grpc://";

        // TODO: This is a temporary solution while we migrate to the new format
        // When set to true the old and the new format will be considered equal
        // Work item to remove this https://dev.azure.com/mseng/1ES/_workitems/edit/2095358
        public static bool OnlyUseHostToCompare = false;

        public static MachineLocation Invalid { get; } = new(string.Empty);

        /// <summary>
        /// Gets whether the current machine location represents valid data
        /// </summary>
        [JsonIgnore]
        public bool IsValid => !string.IsNullOrEmpty(Path);

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

        public bool Equals(MachineLocation other)
        {
            if (Path is null)
            {
                return other.Path is null;
            }

            if (OnlyUseHostToCompare)
            {
                return ExtractHost().Equals(other.ExtractHost(), StringComparison.InvariantCultureIgnoreCase);
            }

            return Path.Equals(other.Path, StringComparison.InvariantCultureIgnoreCase);
        }

        public override int GetHashCode()
        {
            if (Path is null)
            {
                return 42;
            }
            // TODO: This is a temporary solution while we migrate to the new format
            // Same machine has same hash code
            var host = ExtractHost();
            return StringComparer.InvariantCultureIgnoreCase.GetHashCode(host);
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

        private string ExtractHost()
        {
            var (extractedHost, _) = ExtractHostInfo();
            return extractedHost;
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

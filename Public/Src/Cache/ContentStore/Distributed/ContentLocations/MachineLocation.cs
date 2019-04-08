// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Location information for a machine usually represented as UNC path with machine name and a root path.
    /// </summary>
    public readonly struct MachineLocation : IEquatable<MachineLocation>
    {
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

        /// <summary>
        /// Computes a hash for a current machine location.
        /// </summary>
        public ContentHash GetContentHash(IContentHasher hasher)
        {
            Contract.Requires(IsValid, "Please do not use default struct instance");

            return hasher.GetContentHash(Data);
        }

        /// <summary>
        /// Computes a string representation of a hash for a current machine location.
        /// </summary>
        public string GetContentHashString(IContentHasher hasher)
        {
            return GetContentHashString(GetContentHash(hasher));
        }

        /// <inheritdoc />
        public bool Equals(MachineLocation other) => ByteArrayComparer.ArraysEqual(Data, other.Data);

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
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

        private static string GetContentHashString(ContentHash contentHash)
        {
            return Convert.ToBase64String(contentHash.ToHashByteArray());
        }
    }
}

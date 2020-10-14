// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    }
}

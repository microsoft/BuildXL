// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents id of a machine.
    /// </summary>
    public readonly struct MachineId : IEquatable<MachineId>
    {
        /// <summary>
        /// A bit that represents a state of a current machine in a byte array of a cluster state.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Returns an offset in a byte array that represents a state of all the machines in a cluster.
        /// </summary>
        public int GetContentLocationEntryBitOffset()
        {
            // TODO: Make this an extension method (bug 1365340)
            return Index + ContentLocationEntry.BitsInFileSize;
        }

        /// <nodoc />
        public MachineId(int index)
        {
            Contract.Requires(index >= 0);

            Index = index;
        }

        /// <nodoc />
        public static MachineId FromIndex(int index) => new MachineId(index);

        /// <nodoc />
        public static MachineId Deserialize(BinaryReader reader)
        {
            var index = reader.ReadInt32();
            return new MachineId(index);
        }

        /// <nodoc />
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Index);
        }

        /// <inheritdoc />
        public bool Equals(MachineId other)
        {
            return Index == other.Index;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Index;
        }

        /// <nodoc />
        public static bool operator ==(MachineId left, MachineId right)
        {
            return Equals(left, right);
        }

        /// <nodoc />
        public static bool operator !=(MachineId left, MachineId right)
        {
            return !Equals(left, right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Index.ToString();
        }
    }
}

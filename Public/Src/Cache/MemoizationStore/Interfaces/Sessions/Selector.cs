// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     Information combined with a weak fingerprint to yield a strong fingerprint.
    /// </summary>
    public readonly struct Selector : IEquatable<Selector>
    {
        private static readonly ByteArrayComparer ByteComparer = new ByteArrayComparer();

        /// <summary>
        ///     Initializes a new instance of the <see cref="Selector" /> struct.
        /// </summary>
        public Selector(ContentHash contentHash, byte[] output = null)
        {
            ContentHash = contentHash;
            Output = output;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Selector" /> struct.
        /// </summary>
        public Selector(BinaryReader reader)
        {
            Contract.Requires(reader != null);
            ContentHash = new ContentHash(reader);

            var length = reader.ReadInt32();
            Output = length == -1 ? null : reader.ReadBytes(length);
        }

        /// <summary>
        ///     Gets build Engine Input Content Hash.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        ///     Gets build Engine Selector Output, limited to 1kB.
        /// </summary>
        public byte[] Output { get; }

        /// <inheritdoc />
        public bool Equals(Selector other)
        {
            return ContentHash.Equals(other.ContentHash) && ByteArrayComparer.ArraysEqual(Output, other.Output);
        }

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            ContentHash.Serialize(writer);

            if (Output == null)
            {
                writer.Write(-1);
            }
            else
            {
                writer.Write(Output.Length);
                writer.Write(Output);
            }
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return ContentHash.GetHashCode() ^ ByteComparer.GetHashCode(Output);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var hex = Output != null ? HexUtilities.BytesToHex(Output) : string.Empty;
            return $"ContentHash=[{ContentHash}], Output=[{hex}]";
        }

        /// <nodoc />
        public static bool operator ==(Selector left, Selector right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(Selector left, Selector right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        ///     Create a random value.
        /// </summary>
        public static Selector Random(HashType hashType = HashType.Vso0, int outputLength = 2)
        {
            byte[] output = outputLength == 0 ? null : ThreadSafeRandom.GetBytes(outputLength);
            return new Selector(ContentHash.Random(hashType), output);
        }
    }
}

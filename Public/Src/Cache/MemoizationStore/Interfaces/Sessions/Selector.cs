// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Serialization;
using StructUtilities = BuildXL.Cache.ContentStore.Interfaces.Utils.StructUtilities;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     Information combined with a weak fingerprint to yield a strong fingerprint.
    /// </summary>
    public readonly struct Selector : IEquatable<Selector>
    {
        /// <summary>
        /// The usage of the <see cref="Output"/> field is to store data that can be fetched inline along with a
        /// <see cref="StrongFingerprint"/>. There is a limitation on this field because of the underlying storage
        /// limitations.
        ///
        /// The data in this field is interpreted by the user. The cache is blissfully unaware.
        /// </summary>
        public const int MaxOutputLength = 512;

        private static readonly ByteArrayComparer s_byteComparer = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="Selector" /> struct.
        /// </summary>
        public Selector(ContentHash contentHash, byte[]? output = null)
        {
            Contract.Requires(output is null || output.Length <= MaxOutputLength, $"{nameof(output)} can't hold more than {MaxOutputLength} bytes");
            ContentHash = contentHash;
            Output = output;
        }

        /// <summary>
        ///     Gets build Engine Input Content Hash.
        /// </summary>
        public ContentHash ContentHash { get; }

        /// <summary>
        ///     Gets build Engine Selector Output, limited to <see cref="MaxOutputLength"/> bytes.
        /// </summary>
        /// <remarks>
        ///     Although in theory we support <see cref="MaxOutputLength"/> output sizes, in practice this depends on
        ///     the specific implementation being used to store data. Implementors should aim to keep Output as small
        ///     as possible and test in advance that their maximum Output size is supported by the specific cache stack.
        /// </remarks>
        public byte[]? Output { get; }

        /// <inheritdoc />
        public bool Equals(Selector other)
        {
            return ContentHash.Equals(other.ContentHash) && ByteArrayComparer.ArraysEqual(Output, other.Output);
        }

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            ContentHash.Serialize(writer);
            ContentHashList.WriteNullableArray(Output, writer);
        }

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(ref SpanWriter writer)
        {
            writer.EnsureLength(ContentHash.SerializedLength);
            int length = ContentHash.Serialize(writer.Remaining);
            writer.Advance(length);

            ContentHashList.WriteNullableArray(Output, ref writer);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Selector" /> struct.
        /// </summary>
        public static Selector Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var contentHash = new ContentHash(reader);
            var output = ContentHashList.ReadNullableArray(reader);

            return new Selector(contentHash, output);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Selector" /> struct.
        /// </summary>
        public static Selector Deserialize(ref SpanReader reader)
        {
            var contentHash = ContentHash.FromSpan(reader.ReadSpan(ContentHash.SerializedLength));

            var output = ContentHashList.ReadNullableArray(ref reader);

            return new Selector(contentHash, output);
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (ContentHash, s_byteComparer.GetHashCode(Output)).GetHashCode();
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
        public static Selector Random(HashType hashType = HashType.Vso0, int? outputLengthBytes = 2)
        {
            byte[]? output = null;
            if (outputLengthBytes is not null)
            {
                output = ThreadSafeRandom.GetBytes(outputLengthBytes.Value);
            }

            return new Selector(ContentHash.Random(hashType), output);
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Utilities;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     A list of content hashes.
    /// </summary>
    public class ContentHashList : IEquatable<ContentHashList>
    {
        /// <summary>
        /// Separator between HashType and Hash
        /// </summary>
        private const char HashTypeSeparator = ':';

        /// <summary>
        /// Separator between hashes in a HashList
        /// </summary>
        private const char HashListSeparator = ',';

        /// <summary>
        /// Separator between a HashList block which is just list of hashes grouped by type
        /// </summary>
        private const char HashListBlockSeparator = ';';

        private static readonly ByteArrayComparer ByteComparer = new ByteArrayComparer();

        /// <summary>
        ///     The content hashes.
        /// </summary>
        private readonly ContentHash[] _contentHashes;

        /// <summary>
        ///     Custom, application-specific data, limited to 1kB
        /// </summary>
        /// <remarks>
        ///     The size of this member is limited to cap its storage requirements. This metadata
        ///     will typically be stored in databases and bloated metadata will bloat and slow
        ///     access to this metadata. If the application needs to associate more data, it
        ///     should instead use/store a content hash here that refers to content.
        /// </remarks>
        private readonly byte[] _payload;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHashList" /> class.
        /// </summary>
        public ContentHashList(
            ContentHash[] contentHashes,
            byte[] payload = null)
        {
            Contract.Requires(contentHashes != null);

            if (payload?.Length > 1024)
            {
                throw new ArgumentException($"{nameof(ContentHashList)} {nameof(payload)} must be <= 1kB");
            }

            _contentHashes = contentHashes;
            _payload = payload;
        }

        /// <summary>
        ///     Gets copy of content hashes.
        /// </summary>
        public IReadOnlyList<ContentHash> Hashes => _contentHashes;

        /// <summary>
        ///     Gets a value indicating whether payload exists.
        /// </summary>
        public bool HasPayload => _payload != null;

        /// <summary>
        ///     Gets copy of the payload
        /// </summary>
        public IReadOnlyList<byte> Payload => _payload;

        /// <summary>
        ///     Create a random value.
        /// </summary>
        public static ContentHashList Random(
            HashType hashType = HashType.SHA1,
            int contentHashCount = 2,
            byte[] payload = null)
        {
            var contentHashes = Enumerable.Range(0, contentHashCount).Select(x => ContentHash.Random(hashType)).ToArray();
            return new ContentHashList(contentHashes, payload);
        }

        /// <summary>
        ///     Deserialize <see cref="ContentHashList"/>
        /// </summary>
        public static ContentHashList Deserialize(string serializedHashList, byte[] payloadValue)
        {
            if (serializedHashList == null)
            {
                return null;
            }

            var contentHashList = new List<ContentHash>();
            var blocks = serializedHashList.Split(HashListBlockSeparator);
            foreach (var block in blocks.Where(block => block.Length > 0))
            {
                var typeHashesPair = block.Split(HashTypeSeparator);
                Contract.Assert(typeHashesPair.Length == 2);
                var hashType = typeHashesPair[0];
                var hashes = typeHashesPair[1].Split(HashListSeparator);
                foreach (var hash in hashes)
                {
                    var contentHash = new ContentHash(hashType + HashTypeSeparator + hash);
                    contentHashList.Add(contentHash);
                }
            }

            return new ContentHashList(contentHashList.ToArray(), payloadValue);
        }

        /// <inheritdoc />
        public bool Equals(ContentHashList other)
        {
            if (other == null)
            {
                return false;
            }

            if (!ReferenceEquals(_contentHashes, other._contentHashes))
            {
                if (_contentHashes.Length != other._contentHashes.Length)
                {
                    return false;
                }

                if (_contentHashes.Where((t, i) => t != other._contentHashes[i]).Any())
                {
                    return false;
                }
            }

            if (!ReferenceEquals(_payload, other._payload))
            {
                if (_payload == null || other._payload == null)
                {
                    return false;
                }

                if (_payload.Length != other._payload.Length)
                {
                    return false;
                }

                if (!ByteArrayComparer.ArraysEqual(_payload, other._payload))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ContentHashList other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hashCode = ByteComparer.GetHashCode(_payload);
            foreach (var contentHash in _contentHashes)
            {
                unchecked {
                    hashCode = (hashCode * 17) ^ contentHash.GetHashCode();
                }
            }

            return hashCode;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Serialize();
        }

        /// <summary>
        ///     Serialize <see cref="ContentHashList"/>
        /// </summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            if (Hashes?.Count > 0)
            {
                var firstHash = Hashes[0];
                var currentType = firstHash.HashType;
                sb.Append(currentType.Serialize());
                sb.Append(HashTypeSeparator);
                sb.Append(firstHash.ToHex());
                foreach (var contentHash in Hashes.Skip(1))
                {
                    if (contentHash.HashType != currentType)
                    {
                        currentType = contentHash.HashType;
                        sb.Append(HashListBlockSeparator);
                        sb.Append(currentType.Serialize());
                        sb.Append(HashTypeSeparator);
                    }
                    else
                    {
                        sb.Append(HashListSeparator);
                    }

                    sb.Append(contentHash.ToHex());
                }
            }

            return sb.ToString();
        }

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write(_contentHashes, (w, hash) => hash.Serialize(w));
            WriteNullableArray(_payload, writer);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ContentHashList" /> class from its binary representation.
        /// </summary>
        public static ContentHashList Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var contentHashes = reader.ReadArray(r => new ContentHash(r));
            var payload = ReadNullableArray(reader);
            return new ContentHashList(contentHashes, payload);
        }


        /// <nodoc />
        public static void WriteNullableArray(byte[] array, BuildXLWriter writer)
        {
            if (array == null)
            {
                writer.WriteCompact(-1);
            }
            else
            {
                writer.WriteCompact(array.Length);
                writer.Write(array);
            }
        }

        /// <nodoc />
        public static byte[] ReadNullableArray(BuildXLReader reader)
        {
            var payloadLength = reader.ReadInt32Compact();
            byte[] payload = null;
            if (payloadLength >= 0)
            {
                payload = reader.ReadBytes(payloadLength);
            }

            return payload;
        }
    }
}

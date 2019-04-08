// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Security.Cryptography;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Engine.Cache
{
    /// <summary>
    /// Hash algorithm types that <see cref="HashingHelper"/> supports
    /// </summary>
    public enum HashAlgorithmType
    {
        /// <summary>
        /// <see cref="System.Security.Cryptography.SHA1Managed"/>
        /// </summary>
        SHA1Managed = 0, 

        /// <summary>
        /// <see cref="MurmurHashEngine"/>
        /// </summary>
        MurmurHash3 = 1
    }


    internal static class HashAlgorithmExtensions
    {
        public static HashAlgorithm Create(this HashAlgorithmType type)
        {
            switch (type)
            {
                case HashAlgorithmType.SHA1Managed:
                    return new SHA1Managed();
                case HashAlgorithmType.MurmurHash3:
                    return new MurmurHashEngine();
                default:
                    Contract.Assert(false, $"Unknown hash algorithm type: {type}");
                    return null;
            }
        }
    }

    /// <summary>
    /// Helper class for computing fingerprints.
    /// </summary>
    public abstract class CoreHashingHelperBase : IDisposable
    {
        private static readonly char[] NibbleHex =
        { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        /// <summary>
        /// Buffer.
        /// </summary>
        private readonly byte[] m_buffer;

        private PooledObjectWrapper<StringBuilder> m_builderWrapper;

        /// <summary>
        /// Builder for FingerprintInputText
        /// </summary>
        private readonly StringBuilder m_builder;

        /// <summary>
        /// Hash algorithm.
        /// </summary>
        private readonly HashAlgorithm m_engine;

        /// <summary>
        /// Current position to write to in m_buffer
        /// </summary>
        private int m_position;

        /// <summary>
        /// Number of times to indent text
        /// </summary>
        private int m_indentCount;

        /// <summary>
        /// Class constructor.
        /// </summary>
        protected CoreHashingHelperBase(bool recordFingerprintString, HashAlgorithmType hashAlgorithmType = HashAlgorithmType.SHA1Managed)
        {
            m_engine = hashAlgorithmType.Create();
            m_buffer = new byte[4096];
            if (recordFingerprintString)
            {
                m_builderWrapper = Pools.GetStringBuilder();
                m_builder = m_builderWrapper.Instance;
            }
        }

        /// <summary>
        /// Hash size in bytes
        /// </summary>
        public int HashSizeBytes => (int)Math.Ceiling(m_engine.HashSize / (double)8);

        /// <summary>
        /// If recorded, the text that went into the generation of the fingerprint
        /// </summary>
        public string FingerprintInputText => m_builder == null ? null : m_builder.ToString();

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <nodoc />
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_builderWrapper.Instance != null)
                {
                    m_builderWrapper.Dispose();
                }

                if (m_engine != null)
                {
                    m_engine.Dispose();
                }
            }
        }

        /// <summary>
        /// Adds a fingerprint to the fingerprint stream.
        /// </summary>
        public void Add(string name, Fingerprint fingerprint)
        {
            Contract.Requires(name != null);

            BeginItem(HashValueType.ContentHash, name);
            AddInnerFingerprint(fingerprint);
            EndItem();
        }

        /// <summary>
        /// Adds a fingerprint to the fingerprint stream.
        /// </summary>
        public void Add(Fingerprint fingerprint)
        {
            BeginItem(HashValueType.ContentHash);
            AddInnerFingerprint(fingerprint);
            EndItem();
        }

        /// <summary>
        /// Adds a content hash to the fingerprint stream.
        /// </summary>
        public void Add(string name, ContentHash contentHash)
        {
            Contract.Requires(name != null);

            BeginItem(HashValueType.ContentHash, name);
            AddInnerHash(contentHash);
            EndItem();
        }

        /// <summary>
        /// Adds a content hash to the fingerprint stream.
        /// </summary>
        public void Add(ContentHash contentHash)
        {
            BeginItem(HashValueType.ContentHash);
            AddInnerHash(contentHash);
            EndItem();
        }

        /// <summary>
        /// Add the bytes from the string to the fingerprint stream.
        /// </summary>
        public void Add(string name, string text)
        {
            Contract.Requires(name != null);
            Contract.Requires(text != null);

            BeginItem(HashValueType.String, name);
            AddInnerString(text, forceUppercase: false);
            EndItem();
        }

        /// <summary>
        /// Adds an int to the fingerprint stream.
        /// </summary>
        public void Add(string name, int value)
        {
            Contract.Requires(name != null);

            BeginItem(HashValueType.Int, name);
            AddInnerInt32(value);
            EndItem();
        }

        /// <summary>
        /// Adds an int to the fingerprint stream.
        /// </summary>
        public void Add(int value)
        {
            BeginItem(HashValueType.Int);
            AddInnerInt32(value);
            EndItem();
        }

        /// <summary>
        /// Adds a long to the fingerprint stream.
        /// </summary>
        public void Add(string name, long value)
        {
            Contract.Requires(name != null);

            BeginItem(HashValueType.Long, name);
            AddInnerInt64(value);
            EndItem();
        }

        /// <summary>
        /// Generates the final hash value for the whole fingerprint stream.
        /// </summary>
        public Fingerprint GenerateHash()
        {
            byte[] res = GenerateHashBytes();
            return FingerprintUtilities.CreateFrom(res);
        }

        /// <summary>
        /// Generates the final hash value in bytes.
        /// </summary>
        public byte[] GenerateHashBytes()
        {
            Flush();

            m_engine.TransformFinalBlock(m_buffer, 0, 0);

            byte[] res = m_engine.Hash;
            Contract.Assume(res.Length == HashSizeBytes);

            m_engine.Initialize();

            return res;
        }

        private void AddInnerInt64(long val)
        {
            unchecked
            {
                AddInnerInt32((int)(val >> 32));
                AddInnerInt32((int)(val >> 0));
            }
        }

        private void AddInnerInt32(int val)
        {
            AddInnerInt32Debug(val);
            AddInnerInt32Content(val);
        }

        private void AddInnerInt32Debug(int val)
        {
            unchecked
            {
                AddInnerByteDebug((byte)(val >> 24));
                AddInnerByteDebug((byte)(val >> 16));
                AddInnerByteDebug((byte)(val >> 8));
                AddInnerByteDebug((byte)(val >> 0));
            }
        }

        private void AddInnerInt32Content(int val)
        {
            EnsureBufferAvailable(4);
            unchecked
            {
                m_buffer[m_position++] = (byte)(val >> 24);
                m_buffer[m_position++] = (byte)(val >> 16);
                m_buffer[m_position++] = (byte)(val >> 8);
                m_buffer[m_position++] = (byte)(val >> 0);
            }
        }

        private void AddInnerByte(byte val)
        {
            AddInnerByteContent(val);
            if (m_builder != null)
            {
                AddInnerByteDebug(val);
            }
        }

        private void AddInnerByteDebug(byte val)
        {
            if (m_builder != null)
            {
                AddInnerNibbleDebug((byte)(val >> 4));
                AddInnerNibbleDebug((byte)(val & 0xF));
            }
        }

        private void AddInnerByteContent(byte val)
        {
            EnsureBufferAvailable(1);
            m_buffer[m_position++] = val;
        }

        /// <summary>
        /// Adds an inner character
        /// </summary>
        protected void AddInnerCharacter(char character)
        {
            AddInnerCharacterContent(character);

            if (m_builder != null)
            {
                AddInnerCharacterDebug(character);
            }
        }

        private void AddInnerCharacterDebug(char character)
        {
            if (m_builder != null)
            {
                m_builder.Append(character);
            }
        }

        private void AddInnerCharacterContent(char character)
        {
            EnsureBufferAvailable(2);

            m_buffer[m_position++] = unchecked((byte)character);
            m_buffer[m_position++] = unchecked((byte)(character >> 8));
        }

        private void AddInnerNibbleDebug(byte nibble)
        {
            Contract.Requires(nibble < 16);
            AddInnerCharacterDebug(NibbleHex[nibble]);
        }

        /// <summary>
        /// Adds a fingerprint to the fingerprint stream.
        /// </summary>
        protected void AddInnerFingerprint(Fingerprint fingerprint)
        {
            var bytes = fingerprint.ToByteArray();
            Contract.Assert(bytes.Length == FingerprintUtilities.FingerprintLength);

            for (int i = 0; i < bytes.Length; i++)
            {
                AddInnerByte(bytes[i]);
            }
        }

        /// <summary>
        /// Adds a content hash to the fingerprint stream without adding a type prefix.
        /// </summary>
        protected void AddInnerHash(ContentHash contentHash)
        {
            var bytes = contentHash.ToHashByteArray();
            Contract.Assert(bytes.Length == ContentHashingUtilities.HashInfo.ByteLength);

            for (int i = 0; i < ContentHashingUtilities.HashInfo.ByteLength; i++)
            {
                AddInnerByte(bytes[i]);
            }
        }

        /// <summary>
        /// Adds a content hash to the fingerprint stream without adding a type prefix.
        /// </summary>
        protected void AddInnerHash(Fingerprint fingerprint)
        {
            var bytes = fingerprint.ToByteArray();
            Contract.Assert(bytes.Length == ContentHashingUtilities.HashInfo.ByteLength);

            for (int i = 0; i < ContentHashingUtilities.HashInfo.ByteLength; i++)
            {
                AddInnerByte(bytes[i]);
            }
        }

        /// <summary>
        /// Adds a byte array
        /// </summary>
        protected void Add(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                AddInnerByte(bytes[i]);
            }
        }

        /// <summary>
        /// Ends an item
        /// </summary>
        protected void EndItem()
        {
            if (m_builder != null)
            {
                AddInnerCharacterDebug('\r');
                AddInnerCharacterDebug('\n');
            }
        }

        /// <summary>
        /// Begins an item
        /// </summary>
        protected void BeginItem(HashValueType inputType, string name = null)
        {
            if (m_builder != null)
            {
                for (int i = 0; i < m_indentCount; i++)
                {
                    AddInnerCharacterDebug(' ');
                }

                AddInnerCharacterDebug('[');
            }

            if (name != null)
            {
                AddInnerString(name, forceUppercase: false);
                AddInnerCharacter(':');
            }

            switch (inputType)
            {
                case HashValueType.ContentHash:
                    AddInnerStringLiteral("ContentHash", false);
                    break;
                case HashValueType.HashedPath:
                    AddInnerStringLiteral("HashedPath", false);
                    break;
                case HashValueType.Int:
                    AddInnerStringLiteral("Int", false);
                    break;
                case HashValueType.Long:
                    AddInnerStringLiteral("Long", false);
                    break;
                case HashValueType.Path:
                    AddInnerStringLiteral("Path", false);
                    break;
                case HashValueType.String:
                    AddInnerStringLiteral("String", false);
                    break;
                default:
                    Contract.Assert(false, "Unknown HashValueType");
                    break;
            }

            if (m_builder != null)
            {
                AddInnerCharacterDebug(']');
            }
        }

        /// <summary>
        /// Adds an inner string
        /// </summary>
        protected void AddInnerString(string value, bool forceUppercase)
        {
            Contract.Requires(value != null);

            AddInnerCharacterDebug('[');
            AddInnerInt32(value.Length);
            AddInnerCharacterDebug(']');

            AddInnerStringLiteral(value, forceUppercase);
        }

        /// <summary>
        /// Adds an inner string literal
        /// </summary>
        protected void AddInnerStringLiteral(string value, bool forceUppercase)
        {
            Contract.Requires(value != null);

            if (forceUppercase)
            {
                foreach (char ch in value)
                {
                    AddInnerCharacter(ch.ToUpperInvariantFast());
                }
            }
            else
            {
                foreach (char ch in value)
                {
                    AddInnerCharacter(ch);
                }
            }
        }

        /// <summary>
        /// Adds an inner string literal
        /// </summary>
        protected void AddInnerStringLiteralDebug(string value)
        {
            Contract.Requires(value != null);

            foreach (char ch in value)
            {
                AddInnerCharacterDebug(ch);
            }
        }

        private void EnsureBufferAvailable(int size)
        {
            Contract.Requires(size > 0);

            Contract.Assume(size <= m_buffer.Length);
            if (m_position + size > m_buffer.Length)
            {
                Flush();
                m_position = 0;
            }
        }

        private void Flush()
        {
            Contract.Ensures(m_position == 0);

            if (m_position > 0)
            {
                m_engine.TransformBlock(m_buffer, 0, m_position, null, 0);

                m_position = 0;
            }
        }

        /// <summary>
        /// Markers for the types of values being included in the hash
        /// </summary>
        protected enum HashValueType : byte
        {
            /// <summary>
            /// Integer
            /// </summary>
            Int = 0,

            /// <summary>
            /// Long
            /// </summary>
            Long = 1,

            /// <summary>
            /// String
            /// </summary>
            String = 2,

            /// <summary>
            /// Path
            /// </summary>
            Path = 3,

            /// <summary>
            /// Content hash
            /// </summary>
            ContentHash = 4,

            /// <summary>
            /// Hashed path
            /// </summary>
            HashedPath = 5,
        }

        /// <summary>
        /// Increase the indent. Not used in hash computation.
        /// </summary>
        public void Indent()
        {
            m_indentCount++;
        }

        /// <summary>
        /// Decrease the indent.  Not used in hash computation.
        /// </summary>
        public void Unindent()
        {
            m_indentCount--;
        }
    }

    /// <summary>
    /// Helper class for computing fingerprints.
    /// </summary>
    public sealed class CoreHashingHelper : CoreHashingHelperBase
    {
        /// <nodoc />
        public CoreHashingHelper(bool recordFingerprintString)
            : base(recordFingerprintString)
        {
        }
    }
}

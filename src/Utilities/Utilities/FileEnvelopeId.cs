// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;

namespace BuildXL.Utilities
{
    /// <summary>
    /// An identifier for a particular file envelope instance that can be used to correlate a file with other files.
    /// </summary>
    public readonly struct FileEnvelopeId : IEquatable<FileEnvelopeId>
    {
        /// <summary>
        /// Invalid file envelope id.
        /// </summary>
        public static readonly FileEnvelopeId Invalid = new FileEnvelopeId(null);

        internal readonly string Value;

        /// <summary>
        /// Whether this instance is valid
        /// </summary>
        [Pure]
        public bool IsValid => Value != null;

        /// <summary>
        /// Creates a unique id
        /// </summary>
        public static FileEnvelopeId Create()
        {
            return new FileEnvelopeId(Guid.NewGuid());
        }

        /// <summary>
        /// Creates an instance
        /// </summary>
        public FileEnvelopeId(Guid id)
            : this(id.ToString("N"))
        {
        }

        /// <summary>
        /// Creates an instance
        /// </summary>
        public FileEnvelopeId(string id)
        {
            Contract.Requires(id == null || FileEnvelope.IsValidIdentifier(id));
            Value = id;
        }

        /// <nodoc />
        public void Serialize(BinaryWriter writer)
        {
            Contract.Requires(IsValid);
            Contract.Requires(writer != null);
            writer.Write(Value);
        }

        /// <nodoc />
        public static FileEnvelopeId Deserialize(BinaryReader reader)
        {
            Contract.Requires(reader != null);
            var value = reader.ReadString();
            if (!FileEnvelope.IsValidIdentifier(value))
            {
                throw new BuildXLException("Invalid id");
            }

            return new FileEnvelopeId(value);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(FileEnvelopeId other)
        {
            return Value.Equals(other.Value);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Value ?? string.Empty).GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(FileEnvelopeId left, FileEnvelopeId right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FileEnvelopeId left, FileEnvelopeId right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public override string ToString()
        {
            return IsValid ? Value : "<Invalid>";
        }
    }

    /// <summary>
    /// A file envelope that helps with versioning and integrity control of files.
    /// </summary>
    /// <remarks>
    /// The envelope is comprised of a file header with the following features:
    /// - The beginning of the header is stored in a format that can be easily inspected by just calling the "type" command
    /// - The header contains version information
    /// - The header a correlation id that allows correlating a particular file with other files
    /// - The header ends with a magic value that is derived from all other header fields, and the final file size --- therefore providing some protection against files that were not properly finished
    /// </remarks>
    public sealed class FileEnvelope
    {
        /// <summary>
        /// Maximum length of identifier (file envelope name or correlation id)
        /// </summary>
        public const int MaxIdentifierLength = 80;

        /// <summary>
        /// Version that identifies the structure of the envelope itself; increase whenever adding details to the serialization in this fi
        /// </summary>
        private const int Version = 20160323;
        private const string Eol = "\r\n";

        private const string DirtyEof = "*\r\n\u001a";

        // Until the file gets fixed up, there will be a '*' at the end of the visible file header (just "type" the file to see it).
        private const string CleanEof = "\r\n\u001a\u0000";

        private readonly string m_name;
        private readonly int m_version;

        /// <summary>
        /// Checks whether the given string is a valid string representation of an envelope id
        /// </summary>
        /// <remarks>
        /// Only readable ASCII characters are accepted which have a trivial UTF8 encoding.
        /// </remarks>
        [Pure]
        public static bool IsValidIdentifier(string id)
        {
            return
                id != null &&
                id.Length <= MaxIdentifierLength &&
                !id.Any(c => c < 32 || c > 127);
        }

        /// <summary>
        /// Creates an instance of this type
        /// </summary>
        public FileEnvelope(string name, int version)
        {
            Contract.Requires(IsValidIdentifier(name));

            // Some of the computations in this class rely on version being non-negative, so this contract exists
            // To avoid changing the serialization and invalidating all existing FileEnvelopes, the version number is left as an int
            Contract.Requires(version >= 0, "version must be >= 0");

            m_name = name;
            m_version = version;
        }

        /// <summary>
        /// Reads the file header from the stream.
        /// </summary>
        /// <returns>Persisted correlation id</returns>
        /// <exception cref="BuildXLException">Thrown when the file header is incomplete, outdated, or corrupted.</exception>
        public FileEnvelopeId ReadHeader(Stream stream)
        {
            Contract.Requires(stream != null);
            Contract.Ensures(Contract.Result<FileEnvelopeId>().IsValid);

            if (stream.Position != 0)
            {
                throw new BuildXLException("File beginning mismatch");
            }

            try
            {
                using (var reader = new BinaryReader(stream, CharUtilities.Utf8NoBomNoThrow, leaveOpen: true))
                {
                    string persistedName = SafeReadRawString(reader, m_name.Length);
                    if (persistedName != m_name)
                    {
                        throw new BuildXLException("Wrong name");
                    }

                    string persistedEol = SafeReadRawString(reader, Eol.Length);
                    if (persistedEol != Eol)
                    {
                        throw new BuildXLException("Wrong end of line marker");
                    }

                    var buffer = new char[MaxIdentifierLength];
                    int bufferLength = 0;
                    string persistedEof;
                    while (true)
                    {
                        char c = SafeReadChar(reader);
                        if (c == CleanEof[0] || c == DirtyEof[0])
                        {
                            Contract.Assume(CleanEof.Length == DirtyEof.Length);
                            persistedEof = c + SafeReadRawString(reader, CleanEof.Length - 1);
                            break;
                        }

                        if (bufferLength == MaxIdentifierLength)
                        {
                            throw new BuildXLException("Name too long");
                        }

                        buffer[bufferLength++] = c;
                    }

                    var persistedIdString = new string(buffer, 0, bufferLength);
                    if (!IsValidIdentifier(persistedIdString))
                    {
                        throw new BuildXLException("Illegal name");
                    }

                    var persistedId = new FileEnvelopeId(persistedIdString);

                    if (persistedEof == DirtyEof)
                    {
                        throw new BuildXLException("Dirty file!");
                    }

                    if (persistedEof != CleanEof)
                    {
                        throw new BuildXLException("Wrong end of file marker");
                    }

                    var persistedClassVersion = reader.ReadInt32();
                    if (persistedClassVersion != Version)
                    {
                        throw new BuildXLException("Wrong class version");
                    }

                    var persistedInstanceVersion = reader.ReadInt32();
                    if (persistedInstanceVersion != m_version)
                    {
                        throw new BuildXLException("Wrong instance version");
                    }

                    var persistedLength = reader.ReadInt64();
                    var length = reader.BaseStream.Length;
                    if (persistedLength != length)
                    {
                        throw new BuildXLException("Wrong length");
                    }

                    var persistedMagic = reader.ReadInt64();
                    var actualMagic = ComputeMagic(length, persistedId);
                    if (persistedMagic != actualMagic)
                    {
                        throw new BuildXLException("Wrong magic number");
                    }

                    return persistedId;
                }
            }
            catch (IOException ex)
            {
                throw new BuildXLException("Error reading file header", ex);
            }
        }

        private static char SafeReadChar(BinaryReader reader)
        {
            try
            {
                return reader.ReadChar();
            }
            catch (ArgumentException ex)
            {
                throw new BuildXLException("Error reading character", ex);
            }
        }

        private static string SafeReadRawString(BinaryReader reader, int length)
        {
            try
            {
                return new string(reader.ReadChars(length));
            }
            catch (ArgumentException ex)
            {
                throw new BuildXLException("Error reading characters", ex);
            }
        }

        /// <summary>
        /// Checks whether actual and expected ids match
        /// </summary>
        /// <exception cref="BuildXLException">Thrown when the file header is corrupted.</exception>
        public static void CheckCorrelationIds(FileEnvelopeId persistedCorrelationId, FileEnvelopeId expectedCorrelationId)
        {
            Contract.Requires(persistedCorrelationId.IsValid);
            Contract.Requires(expectedCorrelationId.IsValid);

            if (persistedCorrelationId.Value != expectedCorrelationId.Value)
            {
                throw new BuildXLException("Correlation ids don't match");
            }
        }

        private long ComputeMagic(long length, FileEnvelopeId id)
        {
            // HashCodeHelper performs a stable hash code computation.
            // We take into account the other header fields, and the file length.
            return HashCodeHelper.Combine(
                HashCodeHelper.GetOrdinalHashCode64(m_name),
                HashCodeHelper.GetOrdinalHashCode64(id.Value),
                Version | (long)(((ulong)(uint)m_version) << 32),
                length);
        }

        /// <summary>
        /// Writes the header, leaving space for some details to be fixed up later
        /// </summary>
        /// <remarks>
        /// This method must be called at the very beginning of the stream construction.
        /// If stream writing was successful, then <see cref="FixUpHeader"/> must be called at the very end of the stream construction.
        /// </remarks>
        public void WriteHeader(Stream stream, FileEnvelopeId correlationId)
        {
            Contract.Requires(stream != null);
            Contract.Requires(correlationId.IsValid);

            if (stream.Position != 0)
            {
                throw new BuildXLException("File beginning mismatch");
            }

            using (var writer = new BinaryWriter(stream, CharUtilities.Utf8NoBomNoThrow, leaveOpen: true))
            {
                writer.Write(m_name.ToCharArray());
                writer.Write(Eol.ToCharArray());
                writer.Write(correlationId.Value.ToCharArray());

                // Things will need get fixed up starting from here.
                WriteHeaderFixUp(writer, DirtyEof, 0, 0, 0, 0);
            }
        }

        private static void WriteHeaderFixUp(BinaryWriter writer, string eof, int classVersion, int instanceVersion, long length, long magic)
        {
            writer.Write(eof.ToCharArray());
            writer.Write(classVersion);
            writer.Write(instanceVersion);
            writer.Write(length);
            writer.Write(magic);
        }

        /// <summary>
        /// Fills in the magic
        /// </summary>
        /// <remarks>
        /// This method must be called at the very end of the stream construction.
        /// Afterwards, no further stream modifications should occur.
        /// </remarks>
        public void FixUpHeader(Stream stream, FileEnvelopeId correlationId)
        {
            Contract.Requires(stream != null);
            Contract.Requires(correlationId.IsValid);

            var length = stream.Position;

            // Truncate, just in case file already existed but was bigger
            stream.SetLength(stream.Position);

            stream.Position =
                m_name.Length +
                Eol.Length +
                correlationId.Value.Length;

            using (var writer = new BinaryWriter(stream, CharUtilities.Utf8NoBomNoThrow, leaveOpen: true))
            {
                WriteHeaderFixUp(
                    writer,
                    CleanEof,
                    Version,
                    m_version,
                    length,
                    ComputeMagic(length, correlationId));
            }

            stream.Position = length;
        }
    }
}

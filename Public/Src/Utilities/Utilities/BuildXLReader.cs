// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Utilities
{
    /// <summary>
    /// An extended binary reader that can read primitive BuildXL values.
    /// </summary>
    /// <remarks>
    /// This type is internal, as the serialization/deserialization functionality is encapsulated by the PipTable.
    /// </remarks>
    public class BuildXLReader : BinaryReader
    {
        private readonly Stack<int> m_starts = new Stack<int>();
        private readonly bool m_debug;

        /// <summary>
        /// Creates a BuildXLReader
        /// </summary>
        public BuildXLReader(bool debug, Stream stream, bool leaveOpen)
            : base(stream, Encoding.UTF8, leaveOpen)
        {
            m_debug = debug;
        }

        /// <summary>
        /// Creates a non-debug version of a reader.
        /// </summary>
        public static BuildXLReader Create(Stream stream, bool leaveOpen = false)
        {
            Contract.Requires(stream != null);
            return new BuildXLReader(debug: false, stream: stream, leaveOpen: leaveOpen);
        }

        /// <summary>
        /// Start / End methods measure how much memory is used to serialize particular types, and they can add additional Debug information to the payload
        /// </summary>
        [Conditional("MEASURE_PIPTABLE_DETAILS")]
        public void Start<T>()
        {
            if (m_debug)
            {
                int typeId = BuildXLWriterStats.GetTypeId(typeof(T));
                uint marker = ReadUInt32();
                Contract.Assume(marker == BuildXLWriter.ItemStartMarker);
                int s = ReadInt32();
                if (s != typeId)
                {
                    Contract.Assume(false, "Expected " + typeId + " for start of type " + typeof(T).FullName + ". Instead retrieved " + s + " corresponding to type" + BuildXLWriterStats.GetTypeName(s));
                }

                m_starts.Push(typeId);
            }
        }

        /// <summary>
        /// Start / End methods measure how much memory is used to serialize particular types, and they can add additional Debug information to the payload
        /// </summary>
        [Conditional("MEASURE_PIPTABLE_DETAILS")]
        public void End()
        {
            if (m_debug)
            {
                int typeId = m_starts.Pop();
                uint marker = ReadUInt32();
                Contract.Assume(marker == BuildXLWriter.ItemEndMarker);
                int e = ReadInt32();
                if (e != typeId)
                {
                    Contract.Assume(false, "Expected " + typeId + " for end of type " + BuildXLWriterStats.GetTypeName(typeId) + ". Instead retrieved " + e + " corresponding to type" + BuildXLWriterStats.GetTypeName(e));
                }
            }
        }

        /// <summary>
        /// If MEASURE_PIPTABLE_DETAILS is set, indicates current nesting level of serialized data types
        /// </summary>
        public int Depth => m_starts.Count;

        /// <inheritdoc/>
        public override string ReadString()
        {
            Start<string>();
            string value = base.ReadString();
            End();
            return value;
        }

        /// <summary>
        /// Reads an UInt32Compact
        /// </summary>
        public uint ReadUInt32Compact()
        {
            Start<Int32Compact>();
            int value = Read7BitEncodedInt();
            End();
            return unchecked((uint)value);
        }

        /// <summary>
        /// Reads an Int32Compact
        /// </summary>
        public int ReadInt32Compact()
        {
            Start<Int32Compact>();
            int value = Read7BitEncodedInt();
            End();
            return value;
        }

        /// <summary>
        /// Reads the value of a pipId
        /// </summary>
        public virtual uint ReadPipIdValue()
        {
            return ReadUInt32();
        }

        private long Read7BitEncodedLong()
        {
            // Read out an Int64 7 bits at a time.  The high bit
            // of the byte when on means to continue reading more bytes.
            long count = 0;
            int shift = 0;
            byte b;
            do
            {
                // ReadByte handles end of stream cases for us.
                b = ReadByte();
                long m = b & 0x7f;
                count |= m << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);
            return count;
        }

        /// <summary>
        /// Reads an Int64Compact
        /// </summary>
        public long ReadInt64Compact()
        {
            Start<Int64Compact>();
            long value = Read7BitEncodedLong();
            End();
            return value;
        }

        /// <summary>
        /// Reads a Guid
        /// </summary>
        public Guid ReadGuid()
        {
            Start<Guid>();
            var value = new Guid(ReadBytes(16));
            End();
            return value;
        }

        /// <summary>
        /// Reads a StringId
        /// </summary>
        public virtual StringId ReadStringId()
        {
            Start<StringId>();
            var value = new StringId(ReadInt32());
            End();
            return value;
        }

        /// <summary>
        /// Reads a Token
        /// </summary>
        public Token ReadToken()
        {
            Contract.Ensures(Contract.Result<Token>() != null);
            Start<Token>();
            Token value = Token.Deserialize(this);
            End();
            return value;
        }

        /// <summary>
        /// Reads a LocationData
        /// </summary>
        public LocationData ReadLocationData()
        {
            Start<LocationData>();
            LocationData value = LocationData.Deserialize(this);
            End();
            return value;
        }

        /// <summary>
        /// Reads a PathAtom
        /// </summary>
        public virtual PathAtom ReadPathAtom()
        {
            Start<PathAtom>();
            int stringId = ReadInt32();
            PathAtom value = stringId == 0 ? default(PathAtom) : new PathAtom(new StringId(stringId));
            End();
            return value;
        }

        /// <summary>
        /// Reads a SymbolAtom
        /// </summary>
        public virtual SymbolAtom ReadSymbolAtom()
        {
            Start<SymbolAtom>();
            int stringId = ReadInt32();
            SymbolAtom value = stringId == 0 ? default(SymbolAtom) : new SymbolAtom(new StringId(stringId));
            End();
            return value;
        }

        /// <summary>
        /// Reads an AbsolutePath
        /// </summary>
        public virtual AbsolutePath ReadAbsolutePath()
        {
            Start<AbsolutePath>();
            var value = new AbsolutePath(new HierarchicalNameId(ReadInt32()));
            End();
            return value;
        }

        /// <summary>
        /// Reads a RelativePath
        /// </summary>
        public virtual RelativePath ReadRelativePath()
        {
            Start<RelativePath>();
            int length = ReadInt32Compact();
            StringId[] components = new StringId[length];

            for (int i = 0; i < length; i++)
            {
                components[i] = ReadStringId();
            }

            End();
            return new RelativePath(components);
        }

        /// <summary>
        /// Reads a FullSymbol
        /// </summary>
        public virtual FullSymbol ReadFullSymbol()
        {
            Start<FullSymbol>();
            var value = new FullSymbol(new HierarchicalNameId(ReadInt32()));
            End();
            return value;
        }

        /// <summary>
        /// Reads a QualifierId
        /// </summary>
        public virtual QualifierId ReadQualifierId()
        {
            Start<QualifierId>();
            var value = new QualifierId(ReadInt32Compact());
            End();
            return value;
        }

        /// <summary>
        /// Reads a QualifierSpaceId
        /// </summary>
        public virtual QualifierSpaceId ReadQualifierSpaceId()
        {
            Start<QualifierSpaceId>();
            var value = new QualifierSpaceId(ReadInt32Compact());
            End();
            return value;
        }

        /// <summary>
        /// Reads a ModuleId
        /// </summary>
        public virtual ModuleId ReadModuleId()
        {
            Start<ModuleId>();
            var value = new ModuleId(ReadInt32Compact());
            End();
            return value;
        }

        /// <summary>
        /// Reads TokenText
        /// </summary>
        public virtual TokenText ReadTokenText()
        {
            Start<TokenText>();
            var value = new TokenText(new StringId(ReadInt32()));
            End();
            return value;
        }

        /// <summary>
        /// Reads a FileArtifact
        /// </summary>
        public FileArtifact ReadFileArtifact()
        {
            Start<FileArtifact>();
            var value = new FileArtifact(ReadAbsolutePath(), ReadInt32Compact());
            End();
            return value;
        }

        /// <summary>
        /// Reads a <see cref="FileArtifactWithAttributes"/>
        /// </summary>
        public FileArtifactWithAttributes ReadFileArtifactWithAttributes()
        {
            Start<FileArtifactWithAttributes>();
            var value = FileArtifactWithAttributes.Deserialize(this);
            End();
            return value;
        }

        /// <summary>
        /// Reads a DirectoryArtifact
        /// </summary>
        public virtual DirectoryArtifact ReadDirectoryArtifact()
        {
            Start<DirectoryArtifact>();
            // TODO: This can be optimized so a uint can represent both the partial seal id and the isSharedOpaque field
            var value = new DirectoryArtifact(ReadAbsolutePath(), ReadUInt32(), ReadBoolean());
            End();
            return value;
        }

        /// <summary>
        /// Reads FileOrDirectoryArtifact
        /// </summary>        
        public FileOrDirectoryArtifact ReadFileOrDirectoryArtifact()
        {
            Start<FileOrDirectoryArtifact>();
            var isFileArtifact = ReadBoolean();
            var value = isFileArtifact
                ? FileOrDirectoryArtifact.Create(ReadFileArtifact())
                : FileOrDirectoryArtifact.Create(ReadDirectoryArtifact());
            End();
            return value;
        }

        /// <summary>
        /// Reads a ReadOnlyArray
        /// </summary>
        public ReadOnlyArray<T> ReadReadOnlyArray<T>(Func<BuildXLReader, T> reader)
        {
            Contract.Requires(reader != null);
            Start<ReadOnlyArray<T>>();
            int length = ReadInt32Compact();
            if (length == 0)
            {
                End();
                return ReadOnlyArray<T>.Empty;
            }

            T[] array = ReadArrayCore(reader, length);

            End();
            return ReadOnlyArray<T>.FromWithoutCopy(array);
        }

        /// <summary>
        /// Reads a ReadOnlySet
        /// </summary>
        public IReadOnlySet<T> ReadReadOnlySet<T>(Func<BuildXLReader, T> reader)
        {
            Contract.Requires(reader != null);
            Start<IReadOnlySet<T>>();
            int length = ReadInt32Compact();
            if (length == 0)
            {
                End();
                return CollectionUtilities.EmptySet<T>();
            }

            T[] array = ReadArrayCore(reader, length);

            End();
            return new ReadOnlyHashSet<T>(array);
        }

        /// <summary>
        /// Reads an array
        /// </summary>
        public T[] ReadArray<T>(Func<BuildXLReader, T> reader)
        {
            Contract.Requires(reader != null);
            Start<T[]>();
            int length = ReadInt32Compact();
            if (length == 0)
            {
                End();
                return CollectionUtilities.EmptyArray<T>();
            }

            T[] array = ReadArrayCore(reader, length);

            End();
            return array;
        }

        /// <summary>
        /// Reads a ReadOnlyArray
        /// </summary>
        public IReadOnlyList<T> ReadReadOnlyList<T>(Func<BuildXLReader, T> reader)
        {
            Contract.Requires(reader != null);
            Start<IReadOnlyList<T>>();
            int length = ReadInt32Compact();
            T[] array = ReadArrayCore(reader, length);
            End();
            return array;
        }

        private T[] ReadArrayCore<T>(Func<BuildXLReader, T> reader, int length)
        {
            var array = CollectionUtilities.NewOrEmptyArray<T>(length);
            for (int i = 0; i < length; i++)
            {
                array[i] = reader(this);
            }

            return array;
        }

        /// <summary>
        /// Reads a SortedReadOnlyArray
        /// </summary>
        public SortedReadOnlyArray<TValue, TComparer> ReadSortedReadOnlyArray<TValue, TComparer>(
            Func<BuildXLReader, TValue> reader,
            TComparer comparer)
            where TComparer : class, IComparer<TValue>
        {
            Contract.Requires(reader != null);
            Contract.Requires(comparer != null);
            Start<SortedReadOnlyArray<TValue, TComparer>>();
            ReadOnlyArray<TValue> array = ReadReadOnlyArray(reader);
            End();
            return SortedReadOnlyArray<TValue, TComparer>.FromSortedArrayUnsafe(array, comparer);
        }

        /// <summary>
        /// Reads a TimeSpan
        /// </summary>
        public TimeSpan ReadTimeSpan()
        {
            Start<TimeSpan>();
            var value = TimeSpan.FromTicks(Read7BitEncodedLong());
            End();
            return value;
        }

        /// <summary>
        /// Reads a DateTime
        /// </summary>
        public DateTime ReadDateTime()
        {
            Start<DateTime>();
            var value = DateTime.FromBinary(ReadInt64());
            End();
            return value;
        }

        /// <summary>
        /// Reads an <see cref="Encoding"/>.
        /// </summary>
        public Encoding ReadEncoding()
        {
            Start<Encoding>();
            int codePage = ReadInt32();

#if DISABLE_FEATURE_EXTENDED_ENCODING
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
#else
            return Encoding.GetEncoding(codePage);
#endif
        }

        /// <summary>
        /// Reads a Nullable struct
        /// </summary>
        public T? ReadNullableStruct<T>(Func<BuildXLReader, T> reader)
            where T : struct
        {
            Contract.Requires(reader != null);
            Start<T?>();
            T? value = ReadBoolean() ? (T?) reader(this) : (T?) null;
            End();
            return value;
        }

        /// <summary>
        /// Reads a Nullable class
        /// </summary>
        public T ReadNullable<T>(Func<BuildXLReader, T> reader)
            where T : class
        {
            Contract.Requires(reader != null);
            Start<T>();
            T value = ReadBoolean() ? (T) reader(this) : (T) null;
            End();
            return value;
        }

        /// <summary>
        /// Reads a StringTable
        /// </summary>
        public async Task<StringTable> ReadStringTableAsync()
        {
            Start<StringTable>();
            var value = await StringTable.DeserializeAsync(this);
            End();
            return value;
        }

        /// <summary>
        /// Reads a TokenTextTable
        /// </summary>
        public TokenTextTable ReadTokenTextTable()
        {
            Start<TokenTextTable>();
            var value = TokenTextTable.Deserialize(this);
            End();
            return value;
        }

        /// <summary>
        /// Reads a SymbolTable
        /// </summary>
        public async Task<SymbolTable> ReadSymbolTableAsync(Task<StringTable> stringTableTask)
        {
            Contract.Requires(stringTableTask != null);

            Start<SymbolTable>();
            var value = await SymbolTable.DeserializeAsync(this, stringTableTask);
            End();
            return value;
        }

        /// <summary>
        /// Reads a SymbolTable
        /// </summary>
        public async Task<QualifierTable> ReadQualifierTableAsync(Task<StringTable> stringTableTask)
        {
            Contract.Requires(stringTableTask != null);

            Start<SymbolTable>();
            var value = await QualifierTable.DeserializeAsync(this, stringTableTask);
            End();
            return value;
        }

        /// <summary>
        /// Reads a PathTable
        /// </summary>
        public async Task<PathTable> ReadPathTableAsync(Task<StringTable> stringTableTask)
        {
            Contract.Requires(stringTableTask != null);

            Start<PathTable>();
            var value = await PathTable.DeserializeAsync(this, stringTableTask);
            End();
            return value;
        }
    }
}

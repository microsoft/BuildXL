// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Utilities
{
    /// <summary>
    /// An extended binary writer that can write primitive BuildXL values.
    /// </summary>
    public class BuildXLWriter : BinaryWriter
    {
        /// <summary>
        /// Marks the start of an item
        /// </summary>
        public const uint ItemStartMarker = 0xDEADBEEF;

        /// <summary>
        /// Marks the end of an item
        /// </summary>
        public const uint ItemEndMarker = 0xBEEFDEAD;

        private readonly struct Entry
        {
            public readonly long Offset;
            public readonly int TypeId;

            public Entry(long offset, int typeId)
            {
                Offset = offset;
                TypeId = typeId;
            }
        }

        private readonly bool m_debug;
        private readonly bool m_logStats;
        private readonly Stack<Entry> m_starts = new Stack<Entry>();

        /// <summary>
        /// Creates a BuildXLWriter
        /// </summary>
        public BuildXLWriter(bool debug, Stream stream, bool leaveOpen, bool logStats)
            : base(stream, Encoding.UTF8, leaveOpen)
        {
            Contract.Requires(!debug || logStats, "Debug mode requires logstats mode");

            m_debug = debug;
            m_logStats = logStats;
        }

        /// <summary>
        /// Creates a non-debug version of a writer.
        /// </summary>
        public static BuildXLWriter Create(Stream stream, bool leaveOpen = false)
        {
            Contract.Requires(stream != null);
            return new BuildXLWriter(debug: false, stream: stream, leaveOpen: leaveOpen, logStats: false);
        }

        /// <summary>
        /// Start / End methods measure how much memory is used to serialize particular types, and they can add additional Debug information to the payload
        /// </summary>
        [Conditional("MEASURE_PIPTABLE_DETAILS")]
        public void Start<T>()
        {
            int typeId = BuildXLWriterStats.GetTypeId(typeof(T));
            Start(typeId);
        }

        /// <summary>
        /// Start / End methods measure how much memory is used to serialize particular types, and they can add additional Debug information to the payload
        /// </summary>
        [Conditional("MEASURE_PIPTABLE_DETAILS")]
        public void Start(Type type)
        {
            Contract.Requires(type != null);
            int typeId = BuildXLWriterStats.GetTypeId(type);
            Start(typeId);
        }

        private void Start(int typeId)
        {
            if (m_logStats)
            {
                if (m_debug)
                {
                    Write(ItemStartMarker);
                    this.Write(typeId);
                }

                Flush();
                m_starts.Push(new Entry(BaseStream.Position, typeId));
            }
        }

        /// <summary>
        /// Start / End methods measure how much memory is used to serialize particular types, and they can add additional Debug information to the payload
        /// </summary>
        [Conditional("MEASURE_PIPTABLE_DETAILS")]
        public void End()
        {
            if (m_logStats)
            {
                Entry entry = m_starts.Pop();

                if (m_debug)
                {
                    Write(ItemEndMarker);
                    this.Write(entry.TypeId);
                }

                Flush();
                long end = BaseStream.Position;
                long diff = end - entry.Offset;
                BuildXLWriterStats.Add(entry.TypeId, diff);
            }
        }

        /// <summary>
        /// If MEASURE_PIPTABLE_DETAILS is set, indicates current nesting level of serialized data types
        /// </summary>
        public int Depth
        {
            get { return m_starts.Count; }
        }

        /// <summary>
        /// Writes a string
        /// </summary>
        public override void Write(string value)
        {
            Start<string>();
            base.Write(value);
            End();
        }

        /// <summary>
        /// Writes a boolean value
        /// </summary>
        public override void Write(bool value)
        {
            Start<bool>();
            base.Write(value);
            End();
        }

        /// <summary>
        /// Compactly writes an int
        /// </summary>
        public void WriteCompact(int value)
        {
            Start<Int32Compact>();
            Write7BitEncodedInt(value);
            End();
        }

        /// <summary>
        /// Compactly writes an uint
        /// </summary>
        public void WriteCompact(uint value)
        {
            Start<Int32Compact>();
            Write7BitEncodedInt(unchecked((int)value));
            End();
        }

        /// <summary>
        /// Write pip id value to disk.
        /// </summary>
        public virtual void WritePipIdValue(uint value)
        {
            Write(value);
        }

        private void Write7BitEncodedLong(long value)
        {
            unchecked
            {
                // Write out a long 7 bits at a time.  The high bit of the byte,
                // when on, tells reader to continue reading more bytes.
                ulong v = (ulong)value;   // support negative numbers
                while (v >= 0x80)
                {
                    Write((byte)(v | 0x80));
                    v >>= 7;
                }

                Write((byte)v);
            }
        }

        /// <summary>
        /// Compactly writes a long
        /// </summary>
        public void WriteCompact(long value)
        {
            Start<Int64Compact>();
            Write7BitEncodedLong(value);
            End();
        }

        /// <summary>
        /// Writes a Guid
        /// </summary>
        public void Write(Guid value)
        {
            Start<Guid>();
            Write(value.ToByteArray());
            End();
        }

        /// <summary>
        /// Writes a StringId
        /// </summary>
        public virtual void Write(StringId value)
        {
            Start<StringId>();
            Write(value.Value);
            End();
        }

        /// <summary>
        /// Writes an AbsolutePath
        /// </summary>
        public virtual void Write(AbsolutePath value)
        {
            Start<AbsolutePath>();
            Write(value.Value.Value);
            End();
        }

        /// <summary>
        /// Write a RelativePath
        /// </summary>
        public virtual void Write(RelativePath value)
        {
            Start<RelativePath>();
            WriteCompact(value.Components.Length);

            foreach (var component in value.Components)
            {
                Write(component);
            }

            End();
        }

        /// <summary>
        /// Writes TokenText
        /// </summary>
        public virtual void Write(TokenText value)
        {
            Start<TokenText>();
            Write(value.Value.Value);
            End();
        }

        /// <summary>
        /// Writes a FileArtifact
        /// </summary>
        public void Write(FileArtifact value)
        {
            Start<FileArtifact>();
            Write(value.Path);
            WriteCompact(value.RewriteCount);
            End();
        }

        /// <summary>
        /// Writes a <see cref="FileArtifactWithAttributes"/>
        /// </summary>
        public void Write(FileArtifactWithAttributes value)
        {
            Start<FileArtifactWithAttributes>();
            value.Serialize(this);
            End();
        }

        /// <summary>
        /// Writes a DirectoryArtifact
        /// </summary>
        public virtual void Write(DirectoryArtifact value)
        {
            Start<DirectoryArtifact>();
            Write(value.Path);
            Write(value.PartialSealId);
            Write(value.IsSharedOpaque);
            End();
        }

        /// <summary>
        /// Writes a FileOrDirectoryArtifact
        /// </summary>
        public void Write(FileOrDirectoryArtifact value)
        {
            Start<FileOrDirectoryArtifact>();
            Write(value.IsFile);
            if (value.IsFile)
            {
                Write(value.FileArtifact);
            }
            else
            {
                Write(value.DirectoryArtifact);
            }
            End();
        }

        /// <summary>
        /// Writes a ReadOnlyArray
        /// </summary>
        public void Write<T>(ReadOnlyArray<T> value, Action<BuildXLWriter, T> writer)
        {
            Contract.Requires(value.IsValid);
            WriteReadOnlyListCore(value, writer);
        }

        /// <summary>
        /// Writes a ReadOnlySet
        /// </summary>
        public void Write<T>(IReadOnlySet<T> value, Action<BuildXLWriter, T> writer)
        {
            Contract.Requires(value != null);
            WriteReadOnlyListCore(value.ToReadOnlyArray(), writer);
        }

        /// <summary>
        /// Writes an array
        /// </summary>
        public void Write<T>(T[] value, Action<BuildXLWriter, T> writer)
        {
            WriteReadOnlyListCore(value, writer);
        }

        /// <summary>
        /// Writes a ReadOnlyList
        /// </summary>
        public void WriteReadOnlyList<T>(IReadOnlyList<T> value, Action<BuildXLWriter, T> writer)
        {
            Contract.Requires(value != null);
            WriteReadOnlyListCore(value, writer);
        }

        private void WriteReadOnlyListCore<T, TReadOnlyList>(TReadOnlyList value, Action<BuildXLWriter, T> writer)
            where TReadOnlyList : IReadOnlyList<T>
        {
            Contract.Requires(writer != null);
            Start<TReadOnlyList>();
            WriteCompact(value.Count);
            for (int i = 0; i < value.Count; i++)
            {
                writer(this, value[i]);
            }

            End();
        }

        /// <summary>
        /// Writes a custom action
        /// </summary>
        public void Write<T>(T? value, Action<BuildXLWriter, T> writer) where T : struct
        {
            Contract.Requires(writer != null);
            Start<T?>();
            if (value.HasValue)
            {
                Write(true);
                writer(this, value.Value);
            }
            else
            {
                Write(false);
            }

            End();
        }

        /// <summary>
        /// Writes a custom action
        /// </summary>
        public void Write<T>(T value, Action<BuildXLWriter, T> writer) where T : class
        {
            Contract.Requires(writer != null);
            Start<T>();
            if (value != null)
            {
                Write(true);
                writer(this, value);
            }
            else
            {
                Write(false);
            }

            End();
        }

        /// <summary>
        /// Writes a TimeSpan
        /// </summary>
        public void Write(TimeSpan value)
        {
            Start<TimeSpan>();
            Write7BitEncodedLong(value.Ticks);
            End();
        }

        /// <summary>
        /// Writes a DateTime
        /// </summary>
        public void Write(DateTime value)
        {
            Start<DateTime>();
            Write(value.ToBinary());
            End();
        }

        /// <summary>
        /// Writes an encoding.
        /// </summary>
        public void Write(Encoding value)
        {
            Contract.Requires(value != null);
            Start<Encoding>();
            Write(value.CodePage);
            End();
        }

        /// <summary>
        /// Writes a Token
        /// </summary>
        public void Write(Token token)
        {
            Contract.Requires(token != null);
            Start<Token>();
            token.Serialize(this);
            End();
        }

        /// <summary>
        /// Writes a location
        /// </summary>
        public void Write(LocationData value)
        {
            Start<LocationData>();
            value.Serialize(this);
            End();
        }

        /// <summary>
        /// Writes a FullSymbol
        /// </summary>
        public virtual void Write(FullSymbol value)
        {
            Start<FullSymbol>();
            Write(value.Value.Value);
            End();
        }

        /// <summary>
        /// Writes a QualifierId
        /// </summary>
        public virtual void Write(QualifierId value)
        {
            Start<QualifierId>();
            WriteCompact(value.Id);
            End();
        }

        /// <summary>
        /// Writes a QualifierSpaceId
        /// </summary>
        public virtual void Write(QualifierSpaceId value)
        {
            Start<QualifierId>();
            WriteCompact(value.Id);
            End();
        }

        /// <summary>
        /// Writes a ModuleId
        /// </summary>
        public void Write(ModuleId value)
        {
            Start<ModuleId>();
            WriteCompact(value.Value);
            End();
        }

        /// <summary>
        /// Writes a PathAtom
        /// </summary>
        public virtual void Write(PathAtom value)
        {
            Start<PathAtom>();
            Write(value.StringId.Value);
            End();
        }

        /// <summary>
        /// Writes a SymbolAtom
        /// </summary>
        public virtual void Write(SymbolAtom value)
        {
            Start<SymbolAtom>();
            Write(value.StringId.Value);
            End();
        }

        /// <summary>
        /// Writes a SortedReadOnlyArray
        /// </summary>
        public void Write<TValue, TComparer>(SortedReadOnlyArray<TValue, TComparer> value, Action<BuildXLWriter, TValue> writer)
            where TComparer : class, IComparer<TValue>
        {
            Contract.Requires(value.IsValid);
            Contract.Requires(writer != null);
            Start<SortedReadOnlyArray<TValue, TComparer>>();
            Write(value.BaseArray, writer);
            End();
        }

        /// <summary>
        /// Writes a StringTable
        /// </summary>
        public void Write(StringTable value)
        {
            Contract.Requires(value != null);
            Start<StringTable>();
            value.Serialize(this);
            End();
        }

        /// <summary>
        /// Writes a TokenTextTable
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void Write(TokenTextTable value)
        {
            Contract.Requires(value != null);
            Start<TokenTextTable>();
            value.Serialize(this);
            End();
        }

        /// <summary>
        /// Writes a SymbolTable
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void Write(SymbolTable value)
        {
            Contract.Requires(value != null);
            Start<SymbolTable>();
            value.Serialize(this);
            End();
        }

        /// <summary>
        /// Writes a PathTable
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void Write(PathTable value)
        {
            Contract.Requires(value != null);
            Start<PathTable>();
            value.Serialize(this);
            End();
        }
    }

    /// <summary>
    /// Dummy type used as a marker for a special serialization kind
    /// </summary>
    internal enum Int32Compact : byte
    {
    }

    /// <summary>
    /// Dummy type used as a marker for a special serialization kind
    /// </summary>
    internal enum Int64Compact : byte
    {
    }
}

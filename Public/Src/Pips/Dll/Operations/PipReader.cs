// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// An extended binary writer that can read Pips
    /// </summary>
    /// <remarks>
    /// This type is internal, as the serialization/deserialization functionality is encapsulated by the PipTable.
    /// </remarks>
    internal class PipReader : BuildXLReader
    {
        public PipReader(bool debug, StringTable stringTable, Stream stream, bool leaveOpen)
            : base(debug, stream, leaveOpen)
        {
            Contract.Requires(stringTable != null);
            StringTable = stringTable;
        }

        public StringTable StringTable { get; }

        public virtual Pip ReadPip()
        {
            Start<Pip>();
            Pip value = Pip.Deserialize(this);
            End();
            return value;
        }

        public virtual PipProvenance ReadPipProvenance()
        {
            Start<PipProvenance>();
            PipProvenance value = PipProvenance.Deserialize(this);
            End();
            return value;
        }

        public virtual StringId ReadPipDataEntriesPointer()
        {
            return ReadStringId();
        }

        public virtual PipData ReadPipData()
        {
            Start<PipData>();
            PipData value = PipData.Deserialize(this);
            End();
            return value;
        }

        public virtual EnvironmentVariable ReadEnvironmentVariable()
        {
            Start<EnvironmentVariable>();
            EnvironmentVariable value = EnvironmentVariable.Deserialize(this);
            End();
            return value;
        }

        public virtual RegexDescriptor ReadRegexDescriptor()
        {
            Start<RegexDescriptor>();
            RegexDescriptor value = RegexDescriptor.Deserialize(this);
            End();
            return value;
        }

        public virtual PipId ReadPipId()
        {
            Start<PipId>();
            var value = new PipId(base.ReadUInt32());
            End();
            return value;
        }

        public virtual ProcessSemaphoreInfo ReadProcessSemaphoreInfo()
        {
            Start<ProcessSemaphoreInfo>();
            var value = ProcessSemaphoreInfo.Deserialize(this);
            End();
            return value;
        }

        public virtual PipId RemapPipId(PipId pipId) => pipId;

        public virtual WriteFile.Options ReadWriteFileOptions()
        {
            Start<WriteFile.Options>();
            var value = WriteFile.Options.Deserialize(this);
            End();
            return value;
        }

        public virtual IBreakawayChildProcess ReadBreakawayChildProcess()
        {
            Start<IBreakawayChildProcess>();
            var processName = ReadPathAtom();
            var requiredArgs = ReadString();
            var ignoreCase = ReadBoolean();
            End();
            return new BreakawayChildProcess() { ProcessName = processName, RequiredArguments = requiredArgs, RequiredArgumentsIgnoreCase = ignoreCase };
        }

        public virtual ReadOnlyArray<AbsolutePath> ReadDeltaEncodedAbsolutePathArray()
        {
            int length = ReadDeltaEncodedArrayLength<AbsolutePath>();
            var array = CollectionUtilities.NewOrEmptyArray<AbsolutePath>(length);
            int previousPathValue = 0;
            for (int i = 0; i < length; i++)
            {
                var path = ReadPathDelta(previousPathValue);
                array[i] = path;
                previousPathValue = path.RawValue;
            }

            End();
            return ReadOnlyArray<AbsolutePath>.FromWithoutCopy(array);
        }

        public virtual ReadOnlyArray<FileArtifact> ReadDeltaEncodedFileArtifactArray()
        {
            int length = ReadDeltaEncodedArrayLength<FileArtifact>();
            var array = CollectionUtilities.NewOrEmptyArray<FileArtifact>(length);
            int previousPathValue = 0;
            for (int i = 0; i < length; i++)
            {
                Start<FileArtifact>();
                var path = ReadPathDelta(previousPathValue);
                array[i] = new FileArtifact(path, ReadInt32Compact());
                previousPathValue = path.RawValue;
                End();
            }

            End();
            return ReadOnlyArray<FileArtifact>.FromWithoutCopy(array);
        }

        public SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> ReadDeltaEncodedSortedFileArtifactArray()
        {
            Start<SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>>();
            var array = ReadDeltaEncodedFileArtifactArray();
            End();
            return SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>.FromSortedArrayUnsafe(array, OrdinalFileArtifactComparer.Instance);
        }

        public virtual ReadOnlyArray<FileArtifactWithAttributes> ReadDeltaEncodedFileArtifactWithAttributesArray()
        {
            int length = ReadDeltaEncodedArrayLength<FileArtifactWithAttributes>();
            var array = CollectionUtilities.NewOrEmptyArray<FileArtifactWithAttributes>(length);
            int previousPathValue = 0;
            for (int i = 0; i < length; i++)
            {
                Start<FileArtifactWithAttributes>();
                var path = ReadPathDelta(previousPathValue);
                array[i] = FileArtifactWithAttributes.DeserializeMetadata(this, path);
                previousPathValue = path.RawValue;
                End();
            }

            End();
            return ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(array);
        }

        public virtual ReadOnlyArray<DirectoryArtifact> ReadDeltaEncodedDirectoryArtifactArray()
        {
            int length = ReadDeltaEncodedArrayLength<DirectoryArtifact>();
            var array = CollectionUtilities.NewOrEmptyArray<DirectoryArtifact>(length);
            int previousPathValue = 0;
            for (int i = 0; i < length; i++)
            {
                Start<DirectoryArtifact>();
                var path = ReadPathDelta(previousPathValue);
                array[i] = new DirectoryArtifact(path, ReadUInt32());
                previousPathValue = path.RawValue;
                End();
            }

            End();
            return ReadOnlyArray<DirectoryArtifact>.FromWithoutCopy(array);
        }

        public SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer> ReadDeltaEncodedSortedDirectoryArtifactArray()
        {
            Start<SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>>();
            var array = ReadDeltaEncodedDirectoryArtifactArray();
            End();
            return SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>.FromSortedArrayUnsafe(array, OrdinalDirectoryArtifactComparer.Instance);
        }

        private int ReadDeltaEncodedArrayLength<T>()
        {
            Start<ReadOnlyArray<T>>();
            int length = ReadInt32Compact();
            if (length < 0)
            {
                throw new InvalidDataException($"Invalid delta-encoded array length: {length}.");
            }

            return length;
        }

        private AbsolutePath ReadPathDelta(int previousPathValue)
        {
            Start<AbsolutePath>();

            long pathValue;
            if (previousPathValue == AbsolutePath.Invalid.RawValue)
            {
                pathValue = ReadInt32();
            }
            else
            {
                uint zigZagDelta = ReadUInt32Compact();
                int delta = unchecked((int)(zigZagDelta >> 1) ^ -((int)zigZagDelta & 1));
                pathValue = (long)previousPathValue + delta;
                if (pathValue < int.MinValue || pathValue > int.MaxValue)
                {
                    throw new InvalidDataException("Delta-encoded path overflowed the valid AbsolutePath range.");
                }
            }

            if (pathValue == AbsolutePath.Invalid.RawValue)
            {
                throw new InvalidDataException($"Delta-encoded path produced invalid AbsolutePath value {pathValue}.");
            }

            End();
            return new AbsolutePath((int)pathValue);
        }

        /// <summary>
        /// Reads a ReadOnlyArray
        /// </summary>
        public ReadOnlyArray<T> ReadReadOnlyArray<T>(Func<PipReader, T> reader)
        {
            Contract.RequiresNotNull(reader);
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

        private T[] ReadArrayCore<T>(Func<PipReader, T> reader, int length, int minimumLength = 0)
        {
            var array = CollectionUtilities.NewOrEmptyArray<T>(Math.Max(minimumLength, length));
            for (int i = 0; i < length; i++)
            {
                array[i] = reader(this);
            }

            return array;
        }
    }
}

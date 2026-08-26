// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// An extended binary writer that can write Pips
    /// </summary>
    /// <remarks>
    /// This type is internal, as the serialization/deserialization functionality is encapsulated by the PipTable.
    /// </remarks>
    internal class PipWriter : BuildXLWriter
    {
        public PipWriter(bool debug, Stream stream, bool leaveOpen, bool logStats)
            : base(debug, stream, leaveOpen, logStats)
        {
        }

        public virtual void Write(Pip pip)
        {
            Contract.Requires(pip != null);
            Start<Pip>();
            pip.Serialize(this);
            End();
        }

        public virtual void Write(in PipData value)
        {
            Start<PipData>();
            value.Serialize(this);
            End();
        }

        public virtual void WritePipDataEntriesPointer(in StringId value)
        {
            Write(value);
        }

        public virtual void Write(in EnvironmentVariable value)
        {
            Start<EnvironmentVariable>();
            value.Serialize(this);
            End();
        }

        public virtual void Write(RegexDescriptor value)
        {
            Start<RegexDescriptor>();
            value.Serialize(this);
            End();
        }

        public virtual void Write(PipProvenance value)
        {
            Contract.Requires(value != null);
            Start<PipProvenance>();
            value.Serialize(this);
            End();
        }

        public virtual void Write(PipId value)
        {
            Start<PipId>();
            Write(value.Value);
            End();
        }

        public virtual void Write(in ProcessSemaphoreInfo value)
        {
            Start<ProcessSemaphoreInfo>();
            value.Serialize(this);
            End();
        }

        public virtual void Write(in IBreakawayChildProcess value)
        {
            Start<IBreakawayChildProcess>();
            Write(value.ProcessName);
            Write(value.RequiredArguments);
            Write(value.RequiredArgumentsIgnoreCase);
            End();
        }

        public virtual void Write(in WriteFile.Options value)
        {
            Start<WriteFile.Options>();
            value.Serialize(this);
            End();
        }

        // Perf: we may get better compression by sorting paths by RawValue when collection order is not semantically significant.
        public virtual void WriteDeltaEncodedAbsolutePathArray(ReadOnlyArray<AbsolutePath> value)
        {
            Contract.Requires(value.IsValid);
            Start<ReadOnlyArray<AbsolutePath>>();
            WriteCompact(value.Length);

            int previousPathValue = 0;
            for (int i = 0; i < value.Length; i++)
            {
                WritePathDelta(value[i], ref previousPathValue);
            }

            End();
        }

        public virtual void WriteDeltaEncodedFileArtifactArray(ReadOnlyArray<FileArtifact> value)
        {
            Contract.Requires(value.IsValid);
            Start<ReadOnlyArray<FileArtifact>>();
            WriteCompact(value.Length);

            int previousPathValue = 0;
            for (int i = 0; i < value.Length; i++)
            {
                Start<FileArtifact>();
                WritePathDelta(value[i].Path, ref previousPathValue);
                WriteCompact(value[i].RewriteCount);
                End();
            }

            End();
        }

        public void WriteDeltaEncodedFileArtifactArray(SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer> value)
        {
            Contract.Requires(value.IsValid);
            Start<SortedReadOnlyArray<FileArtifact, OrdinalFileArtifactComparer>>();
            WriteDeltaEncodedFileArtifactArray(value.BaseArray);
            End();
        }

        public virtual void WriteDeltaEncodedFileArtifactWithAttributesArray(ReadOnlyArray<FileArtifactWithAttributes> value)
        {
            Contract.Requires(value.IsValid);
            Start<ReadOnlyArray<FileArtifactWithAttributes>>();
            WriteCompact(value.Length);

            int previousPathValue = 0;
            for (int i = 0; i < value.Length; i++)
            {
                Start<FileArtifactWithAttributes>();
                WritePathDelta(value[i].Path, ref previousPathValue);
                value[i].SerializeMetadata(this);
                End();
            }

            End();
        }

        public virtual void WriteDeltaEncodedDirectoryArtifactArray(ReadOnlyArray<DirectoryArtifact> value)
        {
            Contract.Requires(value.IsValid);
            Start<ReadOnlyArray<DirectoryArtifact>>();
            WriteCompact(value.Length);

            int previousPathValue = 0;
            for (int i = 0; i < value.Length; i++)
            {
                Start<DirectoryArtifact>();
                WritePathDelta(value[i].Path, ref previousPathValue);
                Write(value[i].IsSharedOpaquePlusPartialSealId);
                End();
            }

            End();
        }

        public void WriteDeltaEncodedDirectoryArtifactArray(SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer> value)
        {
            Contract.Requires(value.IsValid);
            Start<SortedReadOnlyArray<DirectoryArtifact, OrdinalDirectoryArtifactComparer>>();
            WriteDeltaEncodedDirectoryArtifactArray(value.BaseArray);
            End();
        }

        private void WritePathDelta(AbsolutePath path, ref int previousPathValue)
        {
            Contract.Requires(path.IsValid);
            Start<AbsolutePath>();

            int pathValue = path.RawValue;
            if (previousPathValue == AbsolutePath.Invalid.RawValue)
            {
                base.Write(pathValue);
            }
            else
            {
                int delta = checked(pathValue - previousPathValue);
                uint zigZagDelta = unchecked((uint)((delta << 1) ^ (delta >> 31)));
                WriteCompact(zigZagDelta);
            }

            previousPathValue = pathValue;

            End();
        }

        /// <summary>
        /// Writes a ReadOnlyArray
        /// </summary>
        public void Write<T>(ReadOnlyArray<T> value, Action<PipWriter, T> write)
        {
            Contract.Requires(value.IsValid);
            WriteReadOnlyListCore(value, write);
        }

        private void WriteReadOnlyListCore<T, TReadOnlyList>(TReadOnlyList value, Action<PipWriter, T> write)
            where TReadOnlyList : IReadOnlyList<T>
        {
            Start<TReadOnlyList>();
            WriteCompact(value.Count);
            for (int i = 0; i < value.Count; i++)
            {
                write(this, value[i]);
            }

            End();
        }
    }
}

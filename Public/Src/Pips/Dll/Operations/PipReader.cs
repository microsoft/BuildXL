// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;

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
            Contract.Ensures(Contract.Result<Pip>() != null);
            Start<Pip>();
            Pip value = Pip.Deserialize(this);
            End();
            return value;
        }

        public virtual PipProvenance ReadPipProvenance()
        {
            Contract.Ensures(Contract.Result<PipProvenance>() != null);
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
    }
}

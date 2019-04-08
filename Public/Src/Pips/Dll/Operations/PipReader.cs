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
    internal sealed class PipReader : BuildXLReader
    {
        public PipReader(PageableStore store, Stream stream, bool leaveOpen)
            : base(store.Debug, stream, leaveOpen)
        {
            Contract.Requires(store != null);
            Store = store;
        }

        public PageableStore Store { get; }

        public Pip ReadPip()
        {
            Contract.Ensures(Contract.Result<Pip>() != null);
            Start<Pip>();
            Pip value = Pip.Deserialize(this);
            End();
            return value;
        }

        public PipProvenance ReadPipProvenance()
        {
            Contract.Ensures(Contract.Result<PipProvenance>() != null);
            Start<PipProvenance>();
            PipProvenance value = PipProvenance.Deserialize(this);
            End();
            return value;
        }

        public PipData ReadPipData()
        {
            Start<PipData>();
            PipData value = PipData.Deserialize(this);
            End();
            return value;
        }

        public EnvironmentVariable ReadEnvironmentVariable()
        {
            Start<EnvironmentVariable>();
            EnvironmentVariable value = EnvironmentVariable.Deserialize(this);
            End();
            return value;
        }

        public RegexDescriptor ReadRegexDescriptor()
        {
            Start<RegexDescriptor>();
            RegexDescriptor value = RegexDescriptor.Deserialize(this);
            End();
            return value;
        }

        public PipId ReadPipId()
        {
            Start<PipId>();
            var value = new PipId(ReadUInt32());
            End();
            return value;
        }

        public ProcessSemaphoreInfo ReadProcessSemaphoreInfo()
        {
            Start<ProcessSemaphoreInfo>();
            var value = ProcessSemaphoreInfo.Deserialize(this);
            End();
            return value;
        }
    }
}

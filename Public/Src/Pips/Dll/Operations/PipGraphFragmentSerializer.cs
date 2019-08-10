// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Class to handle serialization/deserialization of pip graph fragments
    /// </summary>
    public class PipGraphFragmentSerializer
    {
        /// <summary>
        /// Total pips to deserialize in the fragment.  Until this is read in from the file, it is 0.
        /// </summary>
        public int TotalPipsToDeserialized => m_totalPipsToDeserialize;

        /// <summary>
        /// Total pips to deserialize in the fragment.  Until this is read in from the file, it is 0.
        /// </summary>
        public int TotalPipsToSerialized => m_totalPipsToSerialize;

        /// <summary>
        /// Total pips deserialized so far.
        /// </summary>
        public int PipsDeserialized => Stats.PipsDeserialized;

        /// <summary>
        /// Total pips serialized so far.
        /// </summary>
        public int PipsSerialized => Stats.PipsSerialized;

        /// <summary>
        /// Description of the fragment, for printing on the console
        /// </summary>
        public string FragmentDescription { get; private set; }

        private readonly PipExecutionContext m_pipExecutionContext;
        private readonly PipGraphFragmentContext m_pipGraphFragmentContext;

        private volatile int m_totalPipsToDeserialize = 0;
        private volatile int m_totalPipsToSerialize = 0;
        
        /// <summary>
        /// Detailed statistics of serialization and deserialization.
        /// </summary>
        public readonly SerializeStats Stats = new SerializeStats();

        /// <summary>
        /// Creates an instance of <see cref="PipGraphFragmentSerializer"/>.
        /// </summary>
        public PipGraphFragmentSerializer(PipExecutionContext pipExecutionContext, PipGraphFragmentContext pipGraphFragmentContext)
        {
            Contract.Requires(pipExecutionContext != null);
            Contract.Requires(pipGraphFragmentContext != null);

            m_pipExecutionContext = pipExecutionContext;
            m_pipGraphFragmentContext = pipGraphFragmentContext;
        }

        /// <summary>
        /// Deserializes a pip graph fragment and call the given handleDeserializedPip function on each pip deserialized.
        /// </summary>
        public bool Deserialize(
            AbsolutePath filePath, 
            Func<PipGraphFragmentContext, PipGraphFragmentProvenance, PipId, Pip, bool> handleDeserializedPip = null, 
            string fragmentDescriptionOverride = null)
        {
            Contract.Requires(filePath.IsValid);
            
            string fileName = filePath.ToString(m_pipExecutionContext.PathTable);
            using (var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new PipRemapReader(m_pipExecutionContext, m_pipGraphFragmentContext, stream))
            {
                string serializedDescription = reader.ReadNullableString();
                FragmentDescription = (fragmentDescriptionOverride ?? serializedDescription) ?? filePath.ToString(m_pipExecutionContext.PathTable);
                var provenance = new PipGraphFragmentProvenance(filePath, FragmentDescription);

                m_totalPipsToDeserialize = reader.ReadInt32();

                for(int i = 0; i < m_totalPipsToDeserialize; i++)
                {
                    var pip = Pip.Deserialize(reader);

                    // Pip id is not deserialized when pip is deserialized.
                    // Pip id must be read separately. To be able to add a pip to the graph, the pip id of the pip
                    // is assumed to be unset, and is set when the pip gets inserted into the pip table.
                    // Thus, one should not assign the pip id of the deserialized pip with the deserialized pip id.
                    // Do not use reader.ReadPipId() for reading the deserialized pip id. The method reader.ReadPipId() 
                    // remaps the pip id to a new pip id.
                    var pipId = new PipId(reader.ReadUInt32());

                    var success = handleDeserializedPip?.Invoke(m_pipGraphFragmentContext, provenance, pipId, pip);

                    if (success.HasValue & !success.Value)
                    {
                        return false;
                    }

                    Stats.Increment(pip, serialize: false);
                }
            }

            return true;
        }

        /// <summary>
        /// Serializes list of pips to a file.
        /// </summary>
        public void Serialize(AbsolutePath filePath, IReadOnlyCollection<Pip> pipsToSerialize, string fragmentDescription = null)
        {
            Contract.Requires(filePath.IsValid);
            Contract.Requires(pipsToSerialize != null);

            string fileName = filePath.ToString(m_pipExecutionContext.PathTable);
            using (var stream = new FileStream(fileName, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new PipRemapWriter(m_pipExecutionContext, m_pipGraphFragmentContext, stream))
            {
                FragmentDescription = fragmentDescription ?? fileName;
                writer.WriteNullableString(FragmentDescription);

                m_totalPipsToSerialize = pipsToSerialize.Count;
                writer.Write(pipsToSerialize.Count);

                foreach (var pip in pipsToSerialize)
                {
                    pip.Serialize(writer);

                    // Pip id is not serialized when pip is serialized. 
                    // Pip id is serialized as part of serializing the pip table. However, since pip table is not
                    // part of graph fragment, then pip id needs to be serialized separately here.
                    writer.Write(pip.PipId.Value);

                    Stats.Increment(pip, serialize: true);
                }
            }
        }

        /// <summary>
        /// Class tracking for statistics of serialization/deserialization.
        /// </summary>
        public class SerializeStats
        {
            private readonly int[] m_pips;
            private readonly int[] m_serviceKinds;
            private int m_serializedPipCount;
            private int m_deserializedPipCount;

            /// <summary>
            /// Number of serialized pips.
            /// </summary>
            public int PipsSerialized => Volatile.Read(ref m_serializedPipCount);

            /// <summary>
            /// Number of deserialized pips.
            /// </summary>
            public int PipsDeserialized => Volatile.Read(ref m_deserializedPipCount);

            /// <summary>
            /// Creates an instance of <see cref="SerializeStats"/>.
            /// </summary>
            public SerializeStats()
            {
                m_pips = new int[(int)PipType.Max];
                m_serviceKinds = new int[(int)Enum.GetValues(typeof(ServicePipKind)).Cast<ServicePipKind>().Max() + 1];
            }

            /// <summary>
            /// Increments stats.
            /// </summary>
            public void Increment(Pip pip, bool serialize)
            {
                if (serialize)
                {
                    Interlocked.Increment(ref m_serializedPipCount);
                }
                else
                {
                    Interlocked.Increment(ref m_deserializedPipCount);
                }

                ++m_pips[(int)pip.PipType];

                if (pip.PipType == PipType.Process)
                {
                    Process process = pip as Process;
                    if (process.ServiceInfo != null && process.ServiceInfo != ServiceInfo.None)
                    {
                        ++m_serviceKinds[(int)process.ServiceInfo.Kind];
                    }
                }
            }

            /// <inheritdoc />
            public override string ToString()
            {
                var builder = new StringBuilder();
                builder.AppendLine();
                builder.AppendLine($"    Serialized pips: {PipsSerialized}");
                builder.AppendLine($"    Deserialized pips: {PipsDeserialized}");
                for (int i = 0; i < m_pips.Length; ++i)
                {
                    PipType pipType = (PipType)i;
                    builder.AppendLine($"    {pipType.ToString()}: {m_pips[i]}");
                    if (pipType == PipType.Process)
                    {
                        for (int j = 0; j < m_serviceKinds.Length; ++j)
                        {
                            ServicePipKind servicePipKind = (ServicePipKind)j;
                            if (servicePipKind != ServicePipKind.None)
                            {
                                builder.AppendLine($"        {servicePipKind.ToString()}: {m_serviceKinds[j]}");
                            }
                        }
                    }
                }

                return builder.ToString();
            }
        }
    }
}

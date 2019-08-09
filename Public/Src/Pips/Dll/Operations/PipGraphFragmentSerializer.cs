// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;

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
        public int PipsDeserialized => Volatile.Read(ref m_deserializedPipCount);

        /// <summary>
        /// Total pips serialized so far.
        /// </summary>
        public int PipsSerialized => Volatile.Read(ref m_serializedPipCount);

        /// <summary>
        /// Description of the fragment, for printing on the console
        /// </summary>
        public string FragmentDescription { get; private set; }

        private readonly PipExecutionContext m_pipExecutionContext;
        private readonly PipGraphFragmentContext m_pipGraphFragmentContext;

        private volatile int m_totalPipsToDeserialize = 0;
        private int m_deserializedPipCount = 0;

        private volatile int m_totalPipsToSerialize = 0;
        private int m_serializedPipCount = 0;

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
            Func<PipGraphFragmentContext, PipGraphFragmentProvenance, Pip, bool> handleDeserializedPip = null,
            string fragmentDescriptionOverride = null)
        {
            Contract.Requires(filePath.IsValid);
            bool successful = true;
            string fileName = filePath.ToString(m_pipExecutionContext.PathTable);
            using (var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new PipRemapReader(m_pipExecutionContext, m_pipGraphFragmentContext, stream))
            {
                string serializedDescription = reader.ReadNullableString();
                FragmentDescription = (fragmentDescriptionOverride ?? serializedDescription) ?? filePath.ToString(m_pipExecutionContext.PathTable);
                var provenance = new PipGraphFragmentProvenance(filePath, FragmentDescription);
                m_totalPipsToDeserialize = reader.ReadInt32();
                try
                {
                    m_deserializedPipCount = 0;
                    while (m_deserializedPipCount < m_totalPipsToDeserialize)
                    {
                        var deserializedPips = new List<Pip>();
                        while (true)
                        {
                            deserializedPips.Add(Pip.Deserialize(reader));

                            // All pips in the same level are added to the graph in parallel
                            // This boolean indicated whether or not the end of the level.
                            bool levelEnd = reader.ReadBoolean();
                            if (levelEnd)
                            {
                                break;
                            }
                        }

                        Parallel.ForEach(deserializedPips, new ParallelOptions(), deserializedPip =>
                        {
                            if (!(handleDeserializedPip?.Invoke(m_pipGraphFragmentContext, provenance, deserializedPip)).Value)
                            {
                                successful = false;
                            }
                            Interlocked.Increment(ref m_deserializedPipCount);
                        });
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
                }
            }

            return successful;
        }

        /// <summary>
        /// Serializes list of pips to a file.
        /// </summary>
        public void Serialize(AbsolutePath filePath, IReadOnlyCollection<IReadOnlyCollection<Pip>> pipsToSerialize, int totalPipCount, string fragmentDescription = null)
        {
            Contract.Requires(filePath.IsValid);
            Contract.Requires(pipsToSerialize != null);

            string fileName = filePath.ToString(m_pipExecutionContext.PathTable);
            using (var stream = new FileStream(fileName, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new PipRemapWriter(m_pipExecutionContext, m_pipGraphFragmentContext, stream))
            {
                FragmentDescription = fragmentDescription ?? fileName;
                writer.WriteNullableString(FragmentDescription);

                writer.Write(totalPipCount);
                m_serializedPipCount = 0;
                foreach (var pipGroup in pipsToSerialize)
                {
                    int i = 0;
                    foreach (var pip in pipGroup)
                    {
                        pip.Serialize(writer);

                        // All pips in the same level are added to the graph in parallel
                        // This boolean indicated whether or not the end of the level.
                        if (i != (pipGroup.Count - 1))
                        {
                            writer.Write(false);
                        }
                        else
                        {
                            writer.Write(true);
                        }

                        i++;
                        Interlocked.Increment(ref m_serializedPipCount);
                    }
                }
            }
        }
    }
}

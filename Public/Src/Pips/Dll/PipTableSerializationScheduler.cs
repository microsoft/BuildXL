// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.Pips
{
    /// <summary>
    /// Schedules serialization tasks for <see cref="PipTable"/>.
    /// </summary>
    internal sealed class PipTableSerializationScheduler : IDisposable
    {
        private readonly Action<Pip, MutablePipState> m_serializer;
        
        private struct QueueItem
        {
            public Pip Pip;
            public MutablePipState Mutable;
        }

        private readonly ActionBlockSlim<QueueItem> m_serializationQueue;

        // Debug hooks - do not do actual serialization and keep the pips alive in a collection.
        private readonly IList<Pip> m_nonSerializablePips;
        private readonly bool m_nonSerializedDebug;

        /// <nodoc />
        public PipTableSerializationScheduler(int maxDegreeOfParallelism, bool debug, Action<Pip, MutablePipState> serializer)
        {
            Contract.Requires(maxDegreeOfParallelism >= -1);
            Contract.Requires(maxDegreeOfParallelism > 0 || debug);

            m_serializer = serializer;

            maxDegreeOfParallelism = maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : maxDegreeOfParallelism;
            m_nonSerializedDebug = maxDegreeOfParallelism == 0 && debug;

            m_serializationQueue = new ActionBlockSlim<QueueItem>(
                m_nonSerializedDebug ? 1 : maxDegreeOfParallelism,
                item => ProcessQueueItem(item));

            if (m_nonSerializedDebug)
            {
                m_serializationQueue.Complete();    // Don't allow more changes
                m_nonSerializablePips = new List<Pip>();
            }
        }

        /// <summary>
        /// Whether this instance got disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Task that completes when no more serialization tasks are running in the background.
        /// </summary>
        public Task WhenDone()
        {
            Contract.Requires(!IsDisposed);

            m_serializationQueue.Complete();
            return m_serializationQueue.CompletionAsync();
        }

        /// <summary>
        /// Disposes this instance; only call <code>WhenDone</code>.
        /// </summary>
        public void Dispose()
        {
            m_serializationQueue.Complete();
            IsDisposed = true;
        }

        /// <summary>
        /// Marks that no further work will be added for serialization.
        /// </summary>
        public void Complete()
        {
            m_serializationQueue.Complete();
        }

        /// <summary>
        /// Adds <paramref name="pip"/> and <paramref name="mutable"/> for serialization.
        /// </summary>
        public void ScheduleSerialization(Pip pip, MutablePipState mutable)
        {
            if (!m_nonSerializedDebug)
            {
                m_serializationQueue.Post(new QueueItem { Pip = pip, Mutable = mutable });
            }
            else
            {
                // Test code! The pips are not serialized. Instead we store them in a list to prevent GC from collecting them.
                lock (m_nonSerializablePips)
                {
                    m_nonSerializablePips.Add(pip);
                }
            }
        }

        /// <summary>
        /// Changes the concurrency level.
        /// </summary>
        public void IncreaseConcurrencyTo(int maxDegreeOfParallelism)
        {
            Contract.Requires(maxDegreeOfParallelism >= -1);

            maxDegreeOfParallelism = maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : maxDegreeOfParallelism;
            if (maxDegreeOfParallelism > m_serializationQueue.DegreeOfParallelism)
            {
                m_serializationQueue.IncreaseConcurrencyTo(maxDegreeOfParallelism);
            }
        }

        private void ProcessQueueItem(QueueItem item)
        {
            m_serializer(item.Pip, item.Mutable);
        }
    }
}

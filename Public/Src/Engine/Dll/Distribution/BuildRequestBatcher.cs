// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Assembles the batches of build requests that are sent to a remote worker in a single PipBuildRequest message.
    /// </summary>
    /// <remarks>
    /// A batch is limited by a maximum number of items and, approximately, by a target serialized size
    /// (see <see cref="BuildXL.Utilities.Configuration.IDistributionConfiguration.GrpcBuildRequestSizeMB"/>).
    /// Items that do not fit in a batch are deferred to the next batch, ahead of newer items, so the order in which
    /// items were queued is preserved.
    ///
    /// Items are only prepared (e.g., their input hashes are extracted) when they are admitted to the batch: preparing
    /// an item can have side effects (e.g., marking hashes as available on the worker), so every prepared item must be
    /// sent in the current batch, and deferred items are prepared again when they are eventually admitted.
    ///
    /// This class is not thread-safe and must only be used by a single thread, except for <see cref="DeferredCount"/>.
    /// </remarks>
    internal sealed class BuildRequestBatcher<T>
    {
        private readonly List<T> m_deferred = new List<T>();
        private readonly List<T> m_notAdmitted = new List<T>();
        private bool[] m_admitted = Array.Empty<bool>();
        private int m_numTakenFromDeferred;
        private volatile int m_deferredCount;

        /// <summary>
        /// The items of the current batch.
        /// </summary>
        /// <remarks>
        /// Populated with deferred items by <see cref="StartBatch"/>; the caller can add new items afterwards.
        /// After <see cref="SelectBySize"/>, it only contains the admitted items.
        /// </remarks>
        public List<T> Batch { get; } = new List<T>();

        /// <summary>
        /// Whether there are deferred items waiting to be added to a batch.
        /// </summary>
        public bool HasDeferred => m_deferred.Count > 0;

        /// <summary>
        /// Number of deferred items waiting to be added to a batch. Safe to read from any thread.
        /// </summary>
        public int DeferredCount => m_deferredCount;

        /// <summary>
        /// Starts a new batch by adding the deferred items, up to <paramref name="maxCount"/> items.
        /// </summary>
        public void StartBatch(int maxCount)
        {
            Batch.Clear();

            m_numTakenFromDeferred = Math.Min(Math.Max(maxCount, 1), m_deferred.Count);
            for (int i = 0; i < m_numTakenFromDeferred; i++)
            {
                Batch.Add(m_deferred[i]);
            }

            m_deferred.RemoveRange(0, m_numTakenFromDeferred);
            m_deferredCount = m_deferred.Count;
        }

        /// <summary>
        /// Prepares the items of the batch in parallel, admitting items until the accumulated size reaches <paramref name="targetBytes"/>.
        /// </summary>
        /// <param name="canDefer">Whether the item can be deferred to the next batch. Items that cannot be deferred are always admitted.</param>
        /// <param name="prepare">Prepares an admitted item and returns its serialized size in bytes.</param>
        /// <param name="targetBytes">The target size of the batch. A non-positive value admits all items.</param>
        /// <param name="maxDegreeOfParallelism">The maximum degree of parallelism for preparing the items.</param>
        /// <returns>The number of deferred items.</returns>
        /// <remarks>
        /// The first item is always admitted so that every batch makes progress, even if that item on its own exceeds the target size.
        /// Items are dispatched in order, so the admitted items are the earliest ones. The admitted size can exceed the target
        /// by the size of the items that are being prepared concurrently when the target is reached.
        /// Afterwards, <see cref="Batch"/> only contains the admitted items, and the remaining ones are deferred to the next batch.
        /// </remarks>
        public int SelectBySize(Func<T, bool> canDefer, Func<T, long> prepare, long targetBytes, int maxDegreeOfParallelism = -1)
        {
            Contract.Requires(canDefer != null);
            Contract.Requires(prepare != null);

            int count = Batch.Count;
            if (m_admitted.Length < count)
            {
                m_admitted = new bool[count];
            }

            bool[] admitted = m_admitted;
            long admittedBytes = 0;
            int nextIndex = -1;

            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism }, _ =>
            {
                // Dispatch the items in order (ignoring the loop index, which Parallel.For partitions into ranges)
                // so that the admitted items are the earliest ones of the batch.
                int i = Interlocked.Increment(ref nextIndex);
                T item = Batch[i];

                if (targetBytes > 0 && i > 0 && Interlocked.Read(ref admittedBytes) >= targetBytes && canDefer(item))
                {
                    admitted[i] = false;
                    return;
                }

                admitted[i] = true;
                Interlocked.Add(ref admittedBytes, prepare(item));
            });

            // Keep the admitted items in the batch and collect the remaining ones, preserving their order.
            m_notAdmitted.Clear();
            int numNotAdmittedFromDeferred = 0;
            int numAdmitted = 0;
            for (int i = 0; i < count; i++)
            {
                if (admitted[i])
                {
                    Batch[numAdmitted++] = Batch[i];
                }
                else
                {
                    m_notAdmitted.Add(Batch[i]);
                    if (i < m_numTakenFromDeferred)
                    {
                        numNotAdmittedFromDeferred++;
                    }
                }
            }

            Batch.RemoveRange(numAdmitted, count - numAdmitted);
            m_numTakenFromDeferred = 0;

            if (m_notAdmitted.Count == 0)
            {
                return 0;
            }

            // Items that came from the deferred list were queued before the ones still in the deferred list (if the previous
            // batch was limited by count), and the new items were queued after them.
            m_deferred.InsertRange(0, m_notAdmitted.GetRange(0, numNotAdmittedFromDeferred));
            m_deferred.AddRange(m_notAdmitted.GetRange(numNotAdmittedFromDeferred, m_notAdmitted.Count - numNotAdmittedFromDeferred));
            m_deferredCount = m_deferred.Count;

            return m_notAdmitted.Count;
        }
    }
}

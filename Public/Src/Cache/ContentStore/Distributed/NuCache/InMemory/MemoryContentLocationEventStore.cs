// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Threading;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// In-memory event store that is used for testing purposes.
    /// </summary>
    public sealed class MemoryContentLocationEventStore : ContentLocationEventStore
    {
        private readonly EventHub _hub;
        private bool _processing = false;
        private readonly ReadWriteLock _lock = ReadWriteLock.Create();
        private OperationContext _context;
        private long _sequenceNumber;

        private readonly BlockingCollection<ContentLocationEventData> _queue = new BlockingCollection<ContentLocationEventData>();

        /// <inheritdoc />
        public MemoryContentLocationEventStore(
            MemoryContentLocationEventStoreConfiguration configuration,
            IContentLocationEventHandler handler,
            CentralStorage centralStorage,
            Interfaces.FileSystem.AbsolutePath workingDirectory)
            : base(configuration, nameof(MemoryContentLocationEventStore), handler, centralStorage, workingDirectory)
        {
            _hub = configuration.Hub;
            _hub.OnEvent += HubOnEvent;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            Tracer.Info(context, "Initializing in-memory content location event store.");

            _context = context;
            return base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _hub.OnEvent -= HubOnEvent;
            _queue.CompleteAdding();

            return base.ShutdownCoreAsync(context);
        }

        private void HubOnEvent(ContentLocationEventData eventData)
        {
            using (_lock.AcquireReadLock())
            {
                if (_processing)
                {
                    DispatchAsync(_context, eventData).GetAwaiter().GetResult();
                    Interlocked.Increment(ref _sequenceNumber);
                }
                else
                {
                    // Processing in suspended enqueue
                    _queue.Add(eventData);
                }
            }
        }

        /// <inheritdoc />
        public override EventSequencePoint GetLastProcessedSequencePoint()
        {
            return new EventSequencePoint(_sequenceNumber);
        }

        /// <inheritdoc />
        protected override BoolResult DoStartProcessing(OperationContext context, EventSequencePoint sequencePoint)
        {
            using (_lock.AcquireWriteLock())
            {
                _processing = true;

                while (_queue.TryTake(out var eventData))
                {
                    DispatchAsync(context, eventData).GetAwaiter().GetResult();
                    Interlocked.Increment(ref _sequenceNumber);
                }
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override BoolResult DoSuspendProcessing(OperationContext context)
        {
            using (_lock.AcquireWriteLock())
            {
                _processing = false;
            }
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> SendEventsCoreAsync(
            OperationContext context,
            ContentLocationEventData[] events,
            CounterCollection<ContentLocationEventStoreCounters> counters)
        {
            foreach (var eventData in events)
            {
                _hub.Send(eventData);
            }

            return BoolResult.SuccessTask;
        }

        /// <summary>
        /// In-memory event hub for communicating between different event store instances in memory.
        /// </summary>
        public sealed class EventHub
        {
            private readonly object _syncLock = new object();

            /// <nodoc />
            public event Action<ContentLocationEventData> OnEvent;

            /// <nodoc />
            public void Send(ContentLocationEventData eventData)
            {
                lock (_syncLock)
                {
                    OnEvent(eventData);
                }
            }

            /// <summary>
            /// The above function, without the lock. Used because of perf benchmarks getting contention on
            /// <see cref="_syncLock"/>. Since it is not part of the usual implementation of EventHub and not
            /// clear it is required for correctness, it is removed here.
            /// </summary>
            public void LockFreeSend(ContentLocationEventData eventData)
            {
                OnEvent(eventData);
            }
        }
    }
}

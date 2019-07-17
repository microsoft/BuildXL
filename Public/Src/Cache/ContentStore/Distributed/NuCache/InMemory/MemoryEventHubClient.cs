// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Threading;
using BuildXL.Utilities.Tracing;
using Microsoft.Azure.EventHubs;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// An event hub client which interacts with a test in-process event hub service
    /// </summary>
    public sealed class MemoryEventHubClient : StartupShutdownSlimBase, IEventHubClient
    {
        private readonly EventHub _hub;
        private readonly ReadWriteLock _lock = ReadWriteLock.Create();
        private OperationContext _context;
        private Action<EventData> _handler;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(MemoryEventHubClient));

        /// <nodoc />
        public MemoryEventHubClient(MemoryContentLocationEventStoreConfiguration configuration)
        {
            _hub = configuration.Hub;
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
            SuspendProcessing(context).ThrowIfFailure();

            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        public BoolResult StartProcessing(OperationContext context, EventSequencePoint sequencePoint, IPartitionReceiveHandler processor)
        {
            using (_lock.AcquireWriteLock())
            {
                _handler = ev => Dispatch(ev, processor);
                var events = _hub.SubscribeAndGetEventsStartingAtSequencePoint(sequencePoint, _handler);

                foreach (var eventData in events)
                {
                    _handler(eventData);
                }
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public BoolResult SuspendProcessing(OperationContext context)
        {
            using (_lock.AcquireWriteLock())
            {
                _hub.Unsubscribe(_handler);
                _handler = null;
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        public Task SendAsync(OperationContext context, EventData eventData)
        {
            _hub.Send(eventData);
            return BoolResult.SuccessTask;
        }

        private void Dispatch(EventData eventData, IPartitionReceiveHandler processor)
        {
            processor.ProcessEventsAsync(new[] { eventData }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// In-memory event hub for communicating between different event store instances in memory.
        /// </summary>
        public sealed class EventHub
        {
            // EventData system property names (copied from event hub codebase)
            private const string EnqueuedTimeUtcName = "x-opt-enqueued-time";
            private const string SequenceNumberName = "x-opt-sequence-number";

            private readonly PropertyInfo _systemPropertiesPropertyInfo = typeof(EventData).GetProperty(nameof(EventData.SystemProperties));
            private readonly List<EventData> _eventStream = new List<EventData>();

            private readonly object _syncLock = new object();

            private event Action<EventData> OnEvent;

            /// <nodoc />
            public void Send(EventData eventData)
            {
                Action<EventData> handler;

                lock (_syncLock)
                {
                    handler = OnEvent;

                    _eventStream.Add(eventData);

                    // HACK: Use reflect to set system properties property since its internal
                    _systemPropertiesPropertyInfo.SetValue(eventData, Activator.CreateInstance(typeof(EventData.SystemPropertiesCollection), nonPublic: true));

                    eventData.SystemProperties[SequenceNumberName] = (long)_eventStream.Count;
                    eventData.SystemProperties[EnqueuedTimeUtcName] = DateTime.UtcNow;
                }

                handler?.Invoke(eventData);
            }

            internal void Unsubscribe(Action<EventData> handler)
            {
                lock (_syncLock)
                {
                    OnEvent -= handler;
                }
            }

            internal IReadOnlyList<EventData> SubscribeAndGetEventsStartingAtSequencePoint(EventSequencePoint sequencePoint, Action<EventData> handler)
            {
                lock (_syncLock)
                {
                    OnEvent += handler;
                    return _eventStream.SkipWhile(eventData => IsBefore(eventData, sequencePoint)).ToArray();
                }
            }

            private bool IsBefore(EventData eventData, EventSequencePoint sequencePoint)
            {
                if (sequencePoint.SequenceNumber != null)
                {
                    return eventData.SystemProperties.SequenceNumber < sequencePoint.SequenceNumber.Value;
                }
                else
                {
                    return eventData.SystemProperties.EnqueuedTimeUtc < sequencePoint.EventStartCursorTimeUtc.Value;
                }
            }
        }
    }
}

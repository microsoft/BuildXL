// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace BuildXL.Cache.ContentStore.Synchronization
{
    /// <summary>
    ///     Class for asynchronous reader-writer lock.
    /// </summary>
    /// <remarks>
    ///     This class does not support upgradable lock.
    ///     Reference: https://blogs.msdn.microsoft.com/pfxteam/2012/02/12/building-async-coordination-primitives-part-7-asyncreaderwriterlock/
    /// </remarks>
    public class AsyncReaderWriterLock
    {
        private readonly Task<Releaser> _readerReleaser;
        private readonly Task<Releaser> _writerReleaser;

        private readonly Queue<TaskCompletionSource<Releaser>> _waitingWriters = new Queue<TaskCompletionSource<Releaser>>();
        private TaskCompletionSource<Releaser> _waitingReader = new TaskCompletionSource<Releaser>();
        private int _readersWaiting;
        private int _status;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AsyncReaderWriterLock"/> class.
        /// </summary>
        public AsyncReaderWriterLock()
        {
            _readerReleaser = Task.FromResult(new Releaser(this, false));
            _writerReleaser = Task.FromResult(new Releaser(this, true));
        }

        /// <summary>
        ///     Obtains a reader lock.
        /// </summary>
        public Task<Releaser> ReaderLockAsync()
        {
            lock (_waitingWriters)
            {
                if (_status >= 0 && _waitingWriters.Count == 0)
                {
                    ++_status;
                    return _readerReleaser;
                }
                else
                {
                    ++_readersWaiting;
                    return _waitingReader.Task.ContinueWith(t => t.Result);
                }
            }
        }

        /// <summary>
        ///     Obtains a writer lock.
        /// </summary>
        public Task<Releaser> WriterLockAsync()
        {
            lock (_waitingWriters)
            {
                if (_status == 0)
                {
                    _status = -1;
                    return _writerReleaser;
                }
                else
                {
                    var waiter = new TaskCompletionSource<Releaser>();
                    _waitingWriters.Enqueue(waiter);
                    return waiter.Task;
                }
            }
        }

        private void ReaderRelease()
        {
            TaskCompletionSource<Releaser> toWake = null;

            lock (_waitingWriters)
            {
                --_status;
                if (_status == 0 && _waitingWriters.Count > 0)
                {
                    _status = -1;
                    toWake = _waitingWriters.Dequeue();
                }
            }

            toWake?.SetResult(new Releaser(this, true));
        }

        private void WriterRelease()
        {
            TaskCompletionSource<Releaser> toWake = null;
            bool toWakeIsWriter = false;

            lock (_waitingWriters)
            {
                if (_waitingWriters.Count > 0)
                {
                    toWake = _waitingWriters.Dequeue();
                    toWakeIsWriter = true;
                }
                else if (_readersWaiting > 0)
                {
                    toWake = _waitingReader;
                    _status = _readersWaiting;
                    _readersWaiting = 0;
                    _waitingReader = new TaskCompletionSource<Releaser>();
                }
                else
                {
                    _status = 0;
                }
            }

            toWake?.SetResult(new Releaser(this, toWakeIsWriter));
        }

        /// <summary>
        ///     Structure for releasing lock.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct Releaser : IDisposable
        {
            private readonly AsyncReaderWriterLock _toRelease;
            private readonly bool _writer;

            /// <summary>
            ///     Initializes a new instance of the <see cref="Releaser"/> struct.
            /// </summary>
            internal Releaser(AsyncReaderWriterLock toRelease, bool writer)
            {
                _toRelease = toRelease;
                _writer = writer;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (_toRelease == null)
                {
                    return;
                }

                if (_writer)
                {
                    _toRelease.WriterRelease();
                }
                else
                {
                    _toRelease.ReaderRelease();
                }
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Bson;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Asynchronous version of <see cref="Mutex"/>. It exposes a <see cref="WaitOneAsync(CancellationToken)"/> that returns a task that completes whenever waiting on the mutex completes.
    /// </summary>
    /// <remarks>
    /// This class is a good alternative when in need of doing inter process synchronization on non-Windows platforms, where the only named synchronization primitive available is the mutex (named semaphores are not supported
    /// yet) and the mutex needs to be used in async code. This class makes sure <see cref="ReleaseMutex()"/> issues a release on the underlying mutex on the same thread where the wait occurred, which is a mutex requirement.
    /// As opposed to a regular mutex, the same thread calling WaitOneAsync() multiple times without releasing the mutex first may be blocked. Similarly, extra calls to releasing the mutex throw an <see cref="ApplicationException"/>.
    /// </remarks>
    public sealed class AsyncMutex : IDisposable
    {
        // The underlying mutex
        private readonly Mutex m_mutex;

        // Event used to wait (on a different thread) for the mutex to be released
        private readonly ManualResetEventSlim m_waitForMutexReleaseEvent;
        // Event used to signal that the mutex was just released
        private readonly AutoResetEvent m_mutexReleasedEvent;
        // A way to force a cancellation when waiting on the mutex on dispose
        private readonly CancellationTokenSource m_cancellationTokenSource;
        private readonly bool m_mutexWasCreated;
        private bool m_threadWaitingOnRelease = false;

        /// <summary>
        /// <see cref="Mutex.Mutex()"/>
        /// </summary>
        public AsyncMutex() : this(name: null)
        { }

        /// <summary>
        /// <see cref="Mutex.Mutex(bool, string?)"/>
        /// </summary>
        public AsyncMutex(string name) : this(name, out _)
        { }

        /// <summary>
        /// <see cref="Mutex.Mutex(bool, string?, out bool)"/>
        /// </summary>
        public AsyncMutex(string name, out bool createdNew)
        {
            m_waitForMutexReleaseEvent = new ManualResetEventSlim(initialState: false);
            m_mutexReleasedEvent = new AutoResetEvent(initialState: false);
            m_mutex = new Mutex(initiallyOwned: false, name, out createdNew);
            m_mutexWasCreated = createdNew;
            m_cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Returns a task that completes whenever the underlying <see cref="WaitHandle.WaitOne()"/> completes
        /// </summary>
        public Task WaitOneAsync(CancellationToken cancellationToken)
        {
            // It is important to run continuations aynchronously since otherwise the same thread created below can be used
            // to run the continuation, which is typically the one releasing the async mutex. This will introduce a deadlock
            // since the thread below is blocked waiting for a ReleaseMutex call
#if NET60_OR_GREATER
            var taskCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
#else
            var taskCompletionSource = new TaskCompletionSource<UnitValue>(TaskCreationOptions.RunContinuationsAsynchronously);
#endif

            // A mutex needs the same thread that waited on the mutex to be the one that releases it. Making sure
            // the wait/release flow happens on the same thread
            new Thread(
                () =>
                {
                    // Chain the user provided cancellation token with the internal cancellation that we use on dispose, so we can use a cancellation token
                    // that represents that any of these two tokens were cancelled
                    using (var chainedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_cancellationTokenSource.Token))
                    {
                        try
                        {
                            // Waiting on multiple handles including a named synchronization primitve is not supported outside of Windows
                            // In that case we wait with a 100ms interval window and check on cancellation tokens on each interation
#if PLATFORM_WIN
                            // Wait on the mutex and the cancellation tokens (the one the user provided, plus the class instance one that is only used for dispose)
                            if (WaitHandle.WaitAny(new[] { m_mutex, chainedCancellation.Token.WaitHandle }) != 0)
                            {
                                // A cancellation was issued. Just return
#if NET60_OR_GREATER
                                taskCompletionSource.SetCanceled(chainedCancellation.Token);                                
#else
                                taskCompletionSource.SetCanceled();
#endif
                                return;
                            }
#else
                                while(!m_mutex.WaitOne(100))
                        {
                            if (chainedCancellation.Token.IsCancellationRequested)
                            {
                                // A cancellation was issued. Just return
#if NET60_OR_GREATER
                                taskCompletionSource.SetCanceled(chainedCancellation.Token);                                
#else
                                taskCompletionSource.SetCanceled();
#endif
                                return;
                            }
                        }
#endif

                            // There is one thread waiting on the mutex release. Let's flag it. Observe this is thread safe since we are inside the mutex critical zone.
                            m_threadWaitingOnRelease = true;

                            // The wait is finished. Flag the task completion source as completed.
#if NET60_OR_GREATER
                            taskCompletionSource.SetResult();
#else
                            taskCompletionSource.SetResult(UnitValue.Unit);
#endif

                            // Now make this thread wait until a ReleaseMutex happens
                            m_waitForMutexReleaseEvent.Wait(chainedCancellation.Token);

                            // A ReleaseMutex was issued. Observe we are still under the mutex critical zone, so at most one thread at a time can reach here.
                            // Reset the release mutex event, so when we actually release the mutex, the next thread that gets it will also wait for the release mutex event.
                            // The main reason we don't use an auto reset event here is because it doesn't take a cancellation token on wait. But besides that, the spirit is the same

                            m_waitForMutexReleaseEvent.Reset();

                            // This thread is not waiting anymore.
                            m_threadWaitingOnRelease = false;

                            // Now release the mutex
                            m_mutex.ReleaseMutex();

                            // And signal that the mutex has been released
                            m_mutexReleasedEvent.Set();
                        }
                        catch (OperationCanceledException)
                        {
                            taskCompletionSource.TrySetCanceled(chainedCancellation.Token);
                        }
                        catch (Exception ex)
                        {
                            taskCompletionSource.TrySetException(ex);
                        }
                    }
                }).Start();

            return taskCompletionSource.Task;
        }

        /// <summary>
        /// <see cref="Mutex.ReleaseMutex()"/>
        /// </summary>
        public void ReleaseMutex()
        {
            ReleaseMutex(disposing: false);
        }

        private void ReleaseMutex(bool disposing)
        {
            // If nobody is waiting for the release of the mutex, then this means the mutex is being released without
            // having been aquired. Throw in this case.
            // Observe that this flag can only track intra-process waits. But that's fine, since it is preventing the ReleaseMutex operation
            // to wait indefinitively (and make it throw instead). If another process tries to release a non-acquired mutex, this flag will be false as well.
            if (!m_threadWaitingOnRelease)
            {
                throw new ApplicationException("An AsyncMutex can only be released if it was previously acquired by the same thread");
            }

            // Signal the release of the mutex, so the mutex thread can proceed.
            m_waitForMutexReleaseEvent.Set();

            // On disposing, don't wait since the mutex might be already released
            if (!disposing)
            {
                // Wait until the mutex is actually released
                m_mutexReleasedEvent.WaitOne();
            }
        }

        /// <inheritoc/>
        public void Dispose()
        {
            // Ensure the mutex task stops waiting for any lock
            m_cancellationTokenSource.Cancel();

            // Ensure the mutex is released
            try
            {
                ReleaseMutex(disposing: true);
            }
            catch (ApplicationException)
            {
                // The mutex was already released
            }

            m_waitForMutexReleaseEvent.Dispose();
            m_cancellationTokenSource.Dispose();

            // If we created the mutex, we dispose it
            if (m_mutexWasCreated)
            {
                m_mutex.Dispose();
            }
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Static utilities related to <see cref="Task" />.
    /// </summary>
    public static class TaskUtilities
    {
        /// <summary>
        /// Creates a TPL Task that is marked as completed when any <see cref="WaitHandle"/> in the array is signaled.
        /// </summary>
        /// <param name="handles">The handles whose signals triggers the task to be completed.  Do not use a <see cref="Mutex"/> here.</param>
        /// <param name="timeout">The timeout (in milliseconds) after which the task will return a value of WaitTimeout.</param>
        /// <returns>A Task that is completed after any handle is signaled.</returns>
        /// <remarks>
        /// There is a (brief) time delay between when the handles are signaled and when the task is marked as completed.
        /// </remarks>
        public static Task<int> ToTask(this WaitHandle[] handles, int timeout = Timeout.Infinite)
        {
            Contract.Requires(handles != null);
            Contract.RequiresForAll(handles, handle => handles != null);

            var tcs = new TaskCompletionSource<int>();
            int signalledHandle = WaitHandle.WaitAny(handles, 0);
            if (signalledHandle != WaitHandle.WaitTimeout)
            {
                // An optimization for if the handle is already signaled
                // to return a completed task.
                tcs.SetResult(signalledHandle);
            }
            else
            {
                var localVariableInitLock = new object();
                lock (localVariableInitLock)
                {
                    RegisteredWaitHandle[] callbackHandles = new RegisteredWaitHandle[handles.Length];
                    for (int i = 0; i < handles.Length; i++)
                    {
                        callbackHandles[i] = ThreadPool.RegisterWaitForSingleObject(
                            handles[i],
                            (state, timedOut) =>
                            {
                                int handleIndex = (int)state;
                                tcs.TrySetResult(timedOut ? WaitHandle.WaitTimeout : handleIndex);

                                // We take a lock here to make sure the outer method has completed setting the local variable callbackHandles contents.
                                lock (localVariableInitLock)
                                {
                                    foreach (var handle in callbackHandles)
                                    {
                                        handle.Unregister(null);
                                    }
                                }
                            },
                            state: i,
                            millisecondsTimeOutInterval: timeout,
                            executeOnlyOnce: true);
                    }
                }
            }

            return tcs.Task;
        }
    }
}

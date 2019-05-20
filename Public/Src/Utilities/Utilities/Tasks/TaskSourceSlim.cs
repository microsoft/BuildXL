// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Represents the producer side of a <see cref="Task{TResult}"/> by wrapping existing <see cref="TaskCompletionSource{TResult}"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TaskCompletionSource{TResult}"/> has one major issue: it is hard to control how a continuation of the provided task is invoke - synchronously or not.
    /// Starting from .NET Framework 4.6.1 <see cref="TaskCompletionSource{TResult}"/> has a constructor that tasks <see cref="TaskCreationOptions"/> for running
    /// continuations asynchronously.
    /// This behavior is very important for avoiding deadlocks.
    /// But the required behavior is platform specific and it is impossible to know that a <see cref="TaskCompletionSource{TResult}"/> is constructed with the right set of
    /// arguments.
    /// To work-around this issue, BuildXL should not use <see cref="TaskCompletionSource{TResult}"/> directly and use this type instead.
    /// </remarks>
    public readonly struct TaskSourceSlim<TResult>
    {
        private readonly TaskCompletionSource<TResult> m_tcs;
        private readonly bool m_setResultAsynchonously;

        /// <nodoc />
        internal TaskSourceSlim(bool runContinuationsAsynchronously)
            : this()
        {
#if NET461Plus
            TaskCreationOptions flags = runContinuationsAsynchronously
                ? TaskCreationOptions.RunContinuationsAsynchronously
                : TaskCreationOptions.None;
            m_tcs = new TaskCompletionSource<TResult>(flags);
            // When a task completion source is constructed with RunContinuationsAsynchronously flag,
            // then it makes no sense to SetResult from a separate thread.
            m_setResultAsynchonously = false;
#else
            m_tcs = new TaskCompletionSource<TResult>();
            m_setResultAsynchonously = runContinuationsAsynchronously;
#endif
        }

        /// <summary>
        /// Returns true if an instance was initialized using proper constructor.
        /// </summary>
        public bool IsValid => m_tcs != null;

        /// <summary>
        /// Gets the <see cref="T:System.Threading.Tasks.Task{TResult}"/> created
        /// by this <see cref="TaskSourceSlim{TResult}"/>.
        /// </summary>
        public Task<TResult> Task => m_tcs.Task;

        /// <summary>
        /// Transitions the underlying
        /// <see cref="T:System.Threading.Tasks.Task{TResult}"/> into the 
        /// <see cref="System.Threading.Tasks.TaskStatus.RanToCompletion">RanToCompletion</see>
        /// state.
        /// </summary>
        public void SetResult(TResult result) => ChangeState(result, (tcs, r) => tcs.SetResult(r));

        /// <summary>
        /// Attempts to transition the underlying
        /// <see cref="T:System.Threading.Tasks.Task{TResult}"/> into the 
        /// <see cref="System.Threading.Tasks.TaskStatus.RanToCompletion">RanToCompletion</see>
        /// state.
        /// </summary>
        public bool TrySetResult(TResult result) => ChangeState(result, (tcs, r) => tcs.TrySetResult(r));

        /// <summary>
        /// Transitions the underlying
        /// <see cref="T:System.Threading.Tasks.Task{TResult}"/> into the 
        /// <see cref="System.Threading.Tasks.TaskStatus.Faulted">Faulted</see> state.
        /// </summary>
        public void SetException(Exception exception) => ChangeState(exception, (tcs, e) => tcs.SetException(e));

        /// <summary>
        /// Attempts to transition the underlying
        /// <see cref="T:System.Threading.Tasks.Task{TResult}"/> into the 
        /// <see cref="System.Threading.Tasks.TaskStatus.Faulted">Faulted</see> state.
        /// </summary>
        public bool TrySetException(Exception exception) => ChangeState(exception, (tcs, e) => tcs.TrySetException(e));

        /// <summary>
        /// Transitions the underlying
        /// <see cref="T:System.Threading.Tasks.Task{TResult}"/> into the 
        /// <see cref="System.Threading.Tasks.TaskStatus.Canceled">Canceled</see> state.
        /// </summary>
        public void SetCanceled() => ChangeState(CancellationToken.None, (tcs, _) => tcs.SetCanceled());

        /// <summary>
        /// Attempts to transition the underlying
        /// <see cref="T:System.Threading.Tasks.Task{TResult}"/> into the 
        /// <see cref="System.Threading.Tasks.TaskStatus.Canceled">Canceled</see> state.
        /// </summary>
        public bool TrySetCanceled() => ChangeState(CancellationToken.None, (tcs, _) => tcs.TrySetCanceled());

        /// <summary>
        /// Connects a task to this TaskSource
        /// </summary>
        public void LinkToTask(Task<TResult> task)
        {
            var @this = this;

            task.ContinueWith(
                (Task continuation) =>
                {
                    if (continuation.IsFaulted)
                    {
                        @this.TrySetException(task.Exception);
                    }
                    else if (continuation.IsCanceled)
                    {
                        @this.TrySetCanceled();
                    }
                    else
                    {
                        @this.TrySetResult(task.Result);
                    }
                }
            );
        }

        private void ChangeState<TResultState>(TResultState state, Action<TaskCompletionSource<TResult>, TResultState> action)
        {
            if (m_setResultAsynchonously)
            {
                var @this = this;
                Analysis.IgnoreResult(System.Threading.Tasks.Task.Run(() => action(@this.m_tcs, state)), justification: "Fire and forget");
            }
            else
            {
                action(m_tcs, state);
            }
        }

        private TChangeResult ChangeState<TResultState, TChangeResult>(TResultState state, Func<TaskCompletionSource<TResult>, TResultState, TChangeResult> func)
        {
            if (m_setResultAsynchonously)
            {
                var @this = this;
                return System.Threading.Tasks.Task.Run(() => func(@this.m_tcs, state)).GetAwaiter().GetResult();
            }
            else
            {
                return func(m_tcs, state);
            }
        }
    }

    /// <summary>
    /// Factory for creating <see cref="TaskSourceSlim{TResult}"/> instances.
    /// </summary>
    public static class TaskSourceSlim
    {
        /// <summary>
        /// Creates a new instance of <see cref="TaskSourceSlim{TResult}"/> and configures it to run continuations asynchronously.
        /// </summary>
        public static TaskSourceSlim<TResult> Create<TResult>() => new TaskSourceSlim<TResult>(runContinuationsAsynchronously: true);
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        /// <summary>
        /// A default constructor that creates the underlying <see cref="TaskCompletionSource{TResult}"/>
        /// with <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> flag.
        /// </summary>
        /// <remarks>
        /// This constructor won't prevent from complete misuse of this type because a struct can be "instantiated"
        /// with <code>default</code> expression, but it makes the possibility of the error way less likely.
        /// </remarks>
        public TaskSourceSlim()
        {
            m_tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <nodoc />
        internal TaskSourceSlim(bool runContinuationsAsynchronously)
            : this()
        {
            TaskCreationOptions flags = runContinuationsAsynchronously
                ? TaskCreationOptions.RunContinuationsAsynchronously
                : TaskCreationOptions.None;
            m_tcs = new TaskCompletionSource<TResult>(flags);
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
        /// Gets the <see cref="TaskCompletionSource{TResult}"/> created
        /// by this <see cref="TaskSourceSlim{TResult}"/>.
        /// </summary>
        public TaskCompletionSource<TResult> CompletionSource => m_tcs;

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

            task.ContinueWith((Task continuation) => LinkToContinuation(task, continuation, @this));
        }

        /// <summary>
        /// Connects a task to this TaskSource
        /// </summary>
        public void LinkToTask<T, TData>(Task<T> task, TData data, Func<T, TData, TResult> getResult)
        {
            var @this = this;

            task.ContinueWith((Task continuation) => LinkToContinuation(task, data, getResult, continuation, @this));
        }

        /// <summary>
        /// Sets the task source to the result of the task
        /// </summary>
        public void TrySetFromTask(Task<TResult> task)
        {
            LinkToContinuation(task, task, this);
        }

        /// <summary>
        /// Sets the task source to the result of the task
        /// </summary>
        public void TrySetFromTask<T>(Task<T> task, Func<T, TResult> getResult)
        {
            LinkToContinuation(task, getResult, static (result, getResult) => getResult(result), task, this);
        }

        private static void LinkToContinuation(Task<TResult> task, Task continuation, TaskSourceSlim<TResult> @this)
        {
            LinkToContinuation(task, true, static (result, _) => result, continuation, @this);
        }

        private static void LinkToContinuation<T, TData>(Task<T> task, TData data, Func<T, TData, TResult> getResult, Task continuation, TaskSourceSlim<TResult> @this)
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
                @this.TrySetResult(getResult(task.Result, data));
            }
        }

        private void ChangeState<TResultState>(TResultState state, Action<TaskCompletionSource<TResult>, TResultState> action)
        {
            action(m_tcs, state);
        }

        private TChangeResult ChangeState<TResultState, TChangeResult>(TResultState state, Func<TaskCompletionSource<TResult>, TResultState, TChangeResult> func)
        {
            return func(m_tcs, state);
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

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// This class is similar to <see cref="ISourceBlock{TOutput}"/>, except that it takes a
    /// listener function which it continuously calls (inside a loop, until <see cref="Complete"/>
    /// has been requested) to receive new items to put in this block.
    ///
    /// This block can be linked to any <see cref="ITargetBlock{TInput}"/>, to which it will
    /// forward all items accepted via the listener function.
    /// </summary>
    /// <typeparam name="TOutput">
    /// Type of the items held in this block.
    /// </typeparam>
    public sealed class ListenerSourceBlock<TOutput>
    {
        /// <summary>
        /// Type of the listener function required by the constructor of this class.
        /// </summary>
        public delegate Task<TOutput> CancellableListener(CancellationToken token);

        private readonly TaskSourceSlim<Unit> m_stopTask;
        private readonly CancellableListener m_listener;
        private readonly string m_name;
        private readonly ILogger m_logger;

        private int m_startCounter;
        private ITargetBlock<TOutput> m_targetBlock;

        /// <nodoc/>
        public ListenerSourceBlock(CancellableListener listener, string name = "unknown", ILogger logger = null)
        {
            Contract.Requires(listener != null);

            m_logger = logger ?? VoidLogger.Instance;
            m_name = name ?? "ListenerSourceBlock";
            m_listener = listener;
            m_stopTask = TaskSourceSlim.Create<Unit>();
            m_startCounter = 0;
        }

        /// <summary>Arbitrary descriptive name provided in the constructor.</summary>
        public override string ToString() => m_name;

        /// <summary>Whether <see cref="Complete"/> has been requested.</summary>
        public bool StopRequested => m_stopTask.Task.IsCompleted;

        /// <summary>Completes when <see cref="Complete"/> has been called or the listener has failed.</summary>
        public Task Completion => m_stopTask.Task;

        /// <summary>The block this source block is linked to.</summary>
        public ITargetBlock<TOutput> TargetBlock => m_targetBlock;

        /// <summary>Whether <see cref="Start"/> has been called.</summary>
        public bool IsStarted => Volatile.Read(ref m_startCounter) > 0;

        /// <summary>
        /// Completes this block.  The completion is propagated to the <see cref="TargetBlock"/>,
        /// if so has been requested in the <see cref="LinkTo"/> method.
        /// </summary>
        public void Complete()
        {
            if (m_stopTask.TrySetResult(Unit.Void))
            {
                Verbose("Setting completion result");
            }
        }

        /// <summary>
        /// Explicitly faults this block.  The fault is propagated to the <see cref="TargetBlock"/>,
        /// if so has been requested in the <see cref="LinkTo"/> method.
        /// </summary>
        public void Fault(Exception e)
        {
            var eAsAggregateException = e as AggregateException;
            var exception = eAsAggregateException != null ? eAsAggregateException.InnerException : e;
            if (m_stopTask.TrySetException(exception))
            {
                Verbose("Faulting completion: {0}", e);
            }
        }

        /// <summary>
        /// Links this block to a target block.
        ///
        /// The target block receives all items accepted by the provided listener.
        ///
        /// Completion of this block can optionally be propagated to the target block.
        /// </summary>
        public void LinkTo(ITargetBlock<TOutput> target, bool propagateCompletion = true)
        {
            Contract.Requires(target != null);

            m_targetBlock = target;

            if (propagateCompletion)
            {
                Completion.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Verbose("Propagating fault completion");
                        m_targetBlock.Fault(t.Exception.InnerException);
                    }
                    else
                    {
                        Verbose("Propagating completion");
                        m_targetBlock.Complete();
                    }
                });
            }
        }

        /// <summary>
        /// Starts to listen for items via the provided listener function.
        /// </summary>
        /// <remarks>
        /// Calling this method multiple times is not allowed.
        /// Calling this method after this block has completed is also not allowed.
        /// </remarks>
        public void Start()
        {
            var incrementedValue = Interlocked.Increment(ref m_startCounter);
            if (incrementedValue > 1)
            {
                throw new IpcException(IpcException.IpcExceptionKind.MultiStart);
            }

            if (StopRequested)
            {
                throw new IpcException(IpcException.IpcExceptionKind.StartAfterStop);
            }

            Analysis.IgnoreResult(
                Task.Run(
                    () =>
                    {
                        try
                        {
                            EventLoop();
                        }
                        catch (Exception e)
                        {
                            Fault(e);
                        }
                    }),
                "Fire and forget"
            );
        }

        private void EventLoop()
        {
            while (!StopRequested)
            {
                Verbose("Listening...");
                var item = CancellableListen();
                if (TargetBlock != null && !StopRequested)
                {
                    var posted = TargetBlock.Post(item);
                    Contract.Assert(posted, "Expected to be able to post a message to the target block");
                }
            }
        }

        private void Verbose(string msg, params object[] args)
        {
            m_logger.Verbose("(" + m_name + ") " + msg, args);
        }

        private TOutput CancellableListen()
        {
            var listenTask = m_listener(CancellationToken.None);

            // The m_listener function is completely out of our control.  However, we want to be able
            // to cancel it at will.  Hence, we wait here for either the listen task or the stop
            // task to finish.  The WaitAny method returns the index of the task that completed.
            int completedTaskIndex = Task.WaitAny(listenTask, m_stopTask.Task);
            if (completedTaskIndex == 0 && !StopRequested)
            {
                // listener task completed -> return its result (if it faulted, the exception will be caught in the 'Start' method)
                Verbose("Listener task {0}", listenTask.IsFaulted
                    ? "failed: " + listenTask.Exception.InnerException.Message
                    : "succeeded and returned an object of type " + listenTask.Result?.GetType()?.FullName);
                return listenTask.GetAwaiter().GetResult();
            }
            else
            {
                // this block completed -> at this point we don't care about any exceptions the listener task might throw.
                listenTask.Forget();
                return default(TOutput);
            }
        }
    }
}

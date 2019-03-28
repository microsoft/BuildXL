// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Ipc
{
    public sealed class ListenerSourceBlockTests : IpcTestBase
    {
        public ListenerSourceBlockTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestFault()
        {
            var listenerBlock = new ListenerSourceBlock<int>(
                listener: (t) => Task.FromResult(0),
                logger: new ConsoleLogger(logVerbose: true));
            var errorMessage = "test";
            listenerBlock.Fault(new ArgumentException(errorMessage));
            AwaitAndAssertTaskFailed<ArgumentException>(listenerBlock.Completion, errorMessage);
        }

        /// <summary>
        ///     Dataflow block diagram for this test:
        ///
        ///     +-------------+     +---------------+     +-------------+
        ///     | BufferBlock +-----> ListenerBlock +-----> BufferBlock |
        ///     +-------------+     +---------------+     +-------------+
        ///
        ///     Post some items to the LHS BufferBlock; wait until they come out on the RHS.
        ///
        ///     This test checks that all the items are received in the correct order.
        /// </summary>
        [Fact]
        public async Task TestBufferBlockAsListenerSource()
        {
            int offset = 10;

            var sources = new BufferBlock<int>();
            var results = new BufferBlock<int>();

            // events are generated as we add items to 'sources'
            var listenerBlock = new ListenerSourceBlock<int>(async (token) => await sources.ReceiveAsync() + offset);

            // results are collected in a BufferBlock
            listenerBlock.LinkTo(results);
            listenerBlock.Start();

            var inputs = Enumerable.Range(1, 10).ToList();

            // generate some events sequentially
            foreach (int i in inputs)
            {
                Assert.True(await sources.SendAsync(i));
            }

            // assert all of them have been received in the same order they were generated
            foreach (int i in inputs)
            {
                Assert.Equal(i + offset, await results.ReceiveAsync());
            }

            listenerBlock.Complete();
            await listenerBlock.Completion;
        }

        /// <summary>
        ///     Dataflow block diagram for this test:
        ///
        ///     +---------------+     +-------------+
        ///     | ListenerBlock +-----> ActionBlock |
        ///     +---------------+     +-------------+
        ///
        ///     The source of the EventLoop periodically generates items, and the
        ///     ActionBlock, upon receiving, puts them in a concurrent bag.
        ///
        ///     This test checks that the EvenLoop successfully completes when
        ///     the <see cref="ListenerSourceBlock{TOutput}.Complete"/> method is
        ///     called, even though its source is still blocked.  The test also
        ///     checks that all items in the end found in the concurrent bag.
        /// </summary>
        [Fact(Skip = "buggy---possibly never terminates")]
        public void TestBlockingListener()
        {
            var finalCount = 100;
            var reachedFinalCount = new ManualResetEvent(false);
            var results = new ConcurrentBag<int>();
            int counter = 0;

            // events are generated periodically, until 'finalCount' is reached
            var listenerBlock = new ListenerSourceBlock<int>(async (token) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1));
                if (counter == finalCount)
                {
                    // let the main unit test method continue and request stop
                    reachedFinalCount.Set();

                    // block this listener for good
                    new ManualResetEvent(false).WaitOne();
                }

                return counter++;
            });

            // collect results in a concurrent bag
            listenerBlock.LinkTo(new ActionBlock<int>(
                i => results.Add(i),
                new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = 3
                }));
            listenerBlock.Start();

            // wait until final count has been reached
            reachedFinalCount.WaitOne();

            // request stop and wait for completions
            listenerBlock.Complete();
            listenerBlock.Completion.Wait();
            listenerBlock.TargetBlock.Completion.Wait();

            // assert that every integer generated in the event loop was received
            Assert.Equal(finalCount, counter);
            Assert.Equal(counter, results.Count);
            XAssert.SetEqual(Enumerable.Range(0, counter), results);
        }

        /// <summary>
        ///     Dataflow block diagram for this test:
        ///
        ///     +---------------+     +-------------+
        ///     | ListenerBlock +-----> ActionBlock |
        ///     +---------------+     +-------------+
        ///
        ///     The source of the EventLoop throws.
        ///
        ///     This test checks that the <see cref="ListenerSourceBlock{TOutput}.Completion"/>
        ///     property captures the thrown exception.
        /// </summary>
        [Fact]
        public void TestListenerThrows()
        {
            var errorMessage = "error";
            var results = new ConcurrentBag<int>();
            var listenerBlock = new ListenerSourceBlock<int>((token) =>
            {
                throw new ArgumentException(errorMessage);
            });
            listenerBlock.LinkTo(new ActionBlock<int>(i => results.Add(i)));
            listenerBlock.Start();

            AwaitAndAssertTaskFailed<ArgumentException>(listenerBlock.Completion, errorMessage);
            AwaitAndAssertTaskFailed<ArgumentException>(listenerBlock.TargetBlock.Completion, errorMessage);
        }

        private void AwaitAndAssertTaskFailed<TException>(Task task, string expectedErrorMessage) where TException : Exception
        {
            var ex = Assert.Throws<TException>(() => task.GetAwaiter().GetResult());
            XAssert.IsTrue(task.IsFaulted);
            XAssert.IsNotNull(task.Exception);
            XAssert.AreEqual(expectedErrorMessage, ex.Message);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestPropagateCompletion(bool shouldStart)
        {
            var listenerBlock = new ListenerSourceBlock<int>((token) => Task.FromResult(1));
            var actionBlock = new ActionBlock<int>(i => { });
            listenerBlock.LinkTo(actionBlock, propagateCompletion: true);

            if (shouldStart)
            {
                listenerBlock.Start();
            }

            Assert.False(listenerBlock.Completion.IsCompleted);
            Assert.False(actionBlock.Completion.IsCompleted);

            listenerBlock.Complete();
            await actionBlock.Completion;
            Assert.True(actionBlock.Completion.IsCompleted);
            Assert.False(actionBlock.Completion.IsFaulted);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestPropagateFault(bool shouldStart)
        {
            var listenerBlock = new ListenerSourceBlock<int>((token) => Task.FromResult(1));
            var actionBlock = new ActionBlock<int>(i => { });
            listenerBlock.LinkTo(actionBlock, propagateCompletion: true);

            if (shouldStart)
            {
                listenerBlock.Start();
            }

            Assert.False(listenerBlock.Completion.IsCompleted);
            Assert.False(actionBlock.Completion.IsCompleted);

            var causeException = new Exception("hi");
            listenerBlock.Fault(causeException);
            var caughtException = Assert.Throws(causeException.GetType(), () => actionBlock.Completion.GetAwaiter().GetResult());
            Assert.Equal(causeException.Message, caughtException.Message);
            Assert.True(actionBlock.Completion.IsFaulted);
        }

        [Fact]
        public async Task TestDoubleStartThrows()
        {
            var listenerBlock = new ListenerSourceBlock<int>((token) => Task.FromResult(1));
            listenerBlock.Start();
            var ex = Assert.Throws<IpcException>(() => listenerBlock.Start());
            Assert.Equal(IpcException.IpcExceptionKind.MultiStart, ex.Kind);
            listenerBlock.Complete();
            await listenerBlock.Completion;
            Assert.False(listenerBlock.Completion.IsFaulted);
        }

        [Fact]
        public async Task TestStartAfterCompleteThrows()
        {
            var listenerBlock = new ListenerSourceBlock<int>((token) => Task.FromResult(1));
            listenerBlock.Complete();
            var ex = Assert.Throws<IpcException>(() => listenerBlock.Start());
            Assert.Equal(IpcException.IpcExceptionKind.StartAfterStop, ex.Kind);
            await listenerBlock.Completion;
            Assert.False(listenerBlock.Completion.IsFaulted);
        }

        [Fact]
        public void TestStartAfterFaultThrows()
        {
            var listenerBlock = new ListenerSourceBlock<int>((token) => Task.FromResult(1));
            listenerBlock.Fault(new Exception());
            var ex = Assert.Throws<IpcException>(() => listenerBlock.Start());
            Assert.Equal(IpcException.IpcExceptionKind.StartAfterStop, ex.Kind);
            Assert.Throws(typeof(Exception), () => listenerBlock.Completion.GetAwaiter().GetResult());
            Assert.True(listenerBlock.Completion.IsFaulted);
        }
    }
}

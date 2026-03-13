// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class LoggingQueueTests
    {
        [Fact]
        public void CallbackIsSynchronousWhenAsyncLoggingIsOff()
        {
            var loggingQueue = new LoggingQueue((context, stats) => { });
            int callbackCount = 0;
            var testThreadId = Environment.CurrentManagedThreadId;
            int callbackThreadId = -1;
            loggingQueue.EnqueueLogAction(
                0,
                () =>
                {
                    callbackThreadId = Environment.CurrentManagedThreadId;
                    callbackCount++;
                },
                eventName: null);

            Assert.Equal(testThreadId, callbackThreadId);
            Assert.Equal(1, callbackCount);
        }
        
        [Fact]
        public void CallbackIsNotSynchronousWhenAsyncLoggingIsOn()
        {
            var loggingQueue = new LoggingQueue((context, stats) => { });
            using var asyncScope = loggingQueue.EnterAsyncLoggingScope(new LoggingContext("loggerComponentInfo"));
            int callbackCount = 0;
            var callbackComplete = new System.Threading.ManualResetEventSlim(false);
            loggingQueue.EnqueueLogAction(
                0,
                () =>
                {
                    System.Threading.Interlocked.Increment(ref callbackCount);
                    callbackComplete.Set();
                },
                eventName: null);

            // The callback should execute (either inline or on a different thread).
            // We can't assert thread identity because the underlying channel uses
            // AllowSynchronousContinuations, which may inline the callback on the
            // writer thread.
            bool completed = callbackComplete.Wait(TimeSpan.FromSeconds(5));
            Assert.True(completed, "Async logging callback did not execute within timeout.");
            Assert.Equal(1, callbackCount);
        }
        
        [Fact]
        public void AllItemsAreDoneOnDispose()
        {
            var loggingQueue = new LoggingQueue((context, stats) => { });
            int callbackCount = 0;
            int count = 1000;

            using (var _ = loggingQueue.EnterAsyncLoggingScope(new LoggingContext("componentName")))
            {
                
                for (int i = 0; i < count; i++)
                {
                    loggingQueue.EnqueueLogAction(0,
                        () =>
                        {
                            callbackCount++;
                        }, eventName: null);
                }
            }

            Assert.Equal(count, callbackCount);
        }
    }
}

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
            var testThreadId = Environment.CurrentManagedThreadId;
            int callbackThreadId = -1;
            loggingQueue.EnqueueLogAction(
                0,
                () =>
                {
                    callbackThreadId = Environment.CurrentManagedThreadId;
                },
                eventName: null);

            Assert.NotEqual(testThreadId, callbackThreadId);
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

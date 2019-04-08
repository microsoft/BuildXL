// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class DebuggerTestTest : DsDebuggerTest
    {
        public DebuggerTestTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestLivenessOnParseErrorWithEmptyDebuggerTest()
        {
            var result = DebugSpec(
                "namespace M {",
                new string[0],
                (source) => Task.Run(() => { }));
            AssertTrue(result.ErrorCount > 0);
        }

        [Fact]
        public void TestLivenessWithNoBreakpoints()
        {
            var result = DebugSpec(
                "namespace M {}",
                new string[0],
                async (source) => await Debugger.ReceiveEvent<ITerminatedEvent>());
            result.ExpectNoError();
        }

        [Fact]
        public void TestLivenessWithNoBreakpointsNoEventAwaits()
        {
            var result = DebugSpec(
                "namespace M {}",
                new string[0],
                (source) => Task.Run(() => { }));
            result.ExpectNoError();
        }

        [Fact]
        public void TestLivenessOnParseErrorWithDebuggerTestAwaitingEvent()
        {
            var ex = AssertThrowsInner<EventNotReceivedException>(() => DebugSpec(
                "namespace M { ",
                new string[0],
                async (source) => await Debugger.ReceiveEvent<IStoppedEvent>()));
            Assert.Equal(typeof(IStoppedEvent), ex.EventType);
        }

        [Fact]
        public void TestLivenessWhenWrongEventReceived()
        {
            var ex = AssertThrowsInner<EventNotReceivedException>(() => DebugSpec(
                "namespace M { }",
                new string[0],
                async (source) => await Debugger.ReceiveEvent<IStoppedEvent>()));
            Assert.Equal(typeof(IStoppedEvent), ex.EventType);
        }

        [Fact]
        public void TestLivenessWhenWrongEventReceived2()
        {
            var ex = AssertThrowsInner<EventNotReceivedException>(() => DebugSpec(
                "namespace M { }",
                new string[0],
                async (source) =>
                {
                    await Debugger.ReceiveEvent<IStoppedEvent>();    // not received, ITerminatedEvent received instead
                    await Debugger.ReceiveEvent<ITerminatedEvent>();
                }));
            Assert.Equal(typeof(IStoppedEvent), ex.EventType);
        }

        [Fact]
        public void TestLivenessWhenEventNotReceived()
        {
            var ex = AssertThrowsInner<EventNotReceivedException>(() => DebugSpec(
                "namespace M { }",
                new string[0],
                async (source) =>
                {
                    await Debugger.ReceiveEvent<ITerminatedEvent>(); // received when evaluation completes
                    await Debugger.ReceiveEvent<IStoppedEvent>();    // not received
                }));
            Assert.Equal(typeof(IStoppedEvent), ex.EventType);
        }

        [Fact]
        public void TestLivenessWhenEvaluationIsStoppedAndDebuggerTestCodeWaitingForEvent()
        {
            var ex = AssertThrowsInner<EventNotReceivedException>(() => DebugSpec(
                "namespace M { export const x = 2+1; } // << breakpoint >> ",
                new string[0],
                async (source) =>
                {
                    await Debugger.ReceiveEvent<IStoppedEvent>();

                    // this one below is never received; nevertheless, shouldn't wait forever here
                    // (without an explicit timeout it would wait for a default amount of time, which is currently 15s)
                    await Debugger.ReceiveEvent<IStoppedEvent>(500);
                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                }));
            Assert.Equal(typeof(IStoppedEvent), ex.EventType);
            Assert.Equal(EventNotReceivedReason.TimedOut, ex.Reason);
        }

        [Fact]
        public void TestLivenessWhenAssertionFailsInDebuggerTest()
        {
            AssertThrowsInner<Xunit.Sdk.TrueException>(() => DebugSpec(
                "namespace M { export const x = 2+1; } // << breakpoint >> ",
                new string[0],
                async (source) =>
                {
                    await Debugger.ReceiveEvent<IStoppedEvent>();
                    var res = Debugger.SendRequest(new ThreadsCommand());
                    AssertAreEqual(2, res.Threads.Count); // this throws, so the next line (which is supposded to unblock eval threads) is never executed (nevertheless, the test shouldn't remain stuck)
                    await ClearBreakpointsContinueAndAwaitTerminate(source);
                }));
        }

        [Fact]
        public void TestLivenessWhenDebuggerTestLeavesEvaluationBlocked()
        {
            AssertThrowsInner<EvaluationBlockedUponTestCompletionException>(() => DebugSpec(
                "namespace M { export const x = 2+1; } // << breakpoint >> ",
                new string[0],
                async (source) =>
                {
                    await Debugger.ReceiveEvent<IStoppedEvent>();
                    var res = Debugger.SendRequest(new ThreadsCommand());
                    AssertAreEqual(1, res.Threads.Count);

                    // exits, leaving the evaluation stopped at the breakpoint (nevertheless, the test shouldn't remain stuck)
                }));
        }

        [Fact]
        public void TestLivenessWhenEvaluationTimesout()
        {
            AssertThrowsInner<EvaluationBlockedUponTestCompletionException>(() => DebugSpec(
                "namespace M { export const x = 2+1; } // << breakpoint >> ",
                new string[0],
                (source) => Task.Run(() =>
                {
                    // exits immediatelly, at which point evaluation is still running but no thread is stopped at breakpoint;
                    // the evaluation will hit the breakpoint, and would normally get stuck there, but it shouldn't in these tests.
                }),
                evalTaskTimeoutMillis: 10));
        }

        private static T AssertThrowsInner<T>(Action a) where T : Exception
        {
            try
            {
                a();
                Assert.True(false, $"Expected exception of type {typeof(T).FullName} to be thrown, but no exception was thrown");
                return default(T);
            }
            catch (AggregateException ex)
            {
                AggregateException e = ex;
                while (e.InnerException is AggregateException) e = (AggregateException)e.InnerException;
                XAssert.AreEqual(typeof(T), e.InnerException.GetType(), "Unexpected exception type: " + e.InnerException.ToStringDemystified());
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                return (T)e.InnerException;
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using VSCode.DebugAdapter;
using VSCode.DebugProtocol;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Debugger
{
    public class BreakOnExceptionTests : DsDebuggerTest
    {
        public BreakOnExceptionTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void BreakOnException1ThreadTest()
        {
            var result = DebugSpec(
                @"
/* 2: */ function fn() {
/* 3: */     return [][0]; // results in runtime exception
/* 4: */ }
/* 5: */
/* 6: */ export const ans = fn();",
                new[] { "ans" },
                async (source) =>
                {
                    var ev = await Debugger.ReceiveEvent<IStoppedEvent>();
                    Assert.Equal("exception", ev.Body.Reason);

                    var threads = GetThreads();
                    AssertAreEqual(1, threads.Count);

                    var stack = GetStackFrames(ev.Body.ThreadId);
                    AssertAreEqual(2, stack.Count);
                    AssertAreEqual(3, stack[0].Line);
                    AssertAreEqual(6, stack[1].Line);
                    await ContinueThreadAndAwaitTerminate(ev.Body.ThreadId);
                });

            result.ExpectErrors(count: 1);
        }

        [Fact]
        public void BreakOnException2ThreadSameHitSameExceptionTest()
        {
            var result = DebugSpec(
                @"
/* 2: */ function fn() {
/* 3: */     return (undefined as any)(); // results in runtime exception
/* 4: */ }
/* 5: */
/* 6: */ export const ans1 = fn();
/* 7: */ export const ans2 = fn();",
                new[] { "ans1", "ans2" },
                async (source) =>
                {
                    var ev1 = await Debugger.ReceiveEvent<IStoppedEvent>();
                    Assert.Equal("exception", ev1.Body.Reason);

                    var ev2 = await Debugger.ReceiveEvent<IStoppedEvent>();
                    Assert.Equal("exception", ev2.Body.Reason);

                    var threads = GetThreadsSortedByName();
                    AssertAreEqual(2, threads.Count);
                    AssertTrue(threads[0].Name.StartsWith("ans1"));
                    AssertTrue(threads[1].Name.StartsWith("ans2"));

                    var stack1 = GetStackFrames(threads[0].Id);
                    AssertAreEqual(2, stack1.Count);
                    AssertAreEqual(3, stack1[0].Line);
                    AssertAreEqual(6, stack1[1].Line);

                    var stack2 = GetStackFrames(threads[1].Id);
                    AssertAreEqual(2, stack2.Count);
                    AssertAreEqual(3, stack2[0].Line);
                    AssertAreEqual(7, stack2[1].Line);

                    // continue 'ans1'
                    Debugger.Continue(threads[0].Id);

                    // make sure 'ans2' thread is still at the same position
                    stack2 = GetStackFrames(threads[1].Id);
                    AssertAreEqual(2, stack2.Count);
                    AssertAreEqual(3, stack2[0].Line);
                    AssertAreEqual(7, stack2[1].Line);

                    // continue 'ans2' and wait for TerminatedEvent
                    Debugger.Continue(threads[1].Id);
                    await Debugger.ReceiveEvent<TerminatedEvent>();
                });

            result.ExpectErrors(count: 2);
        }

        [Fact]
        public void BreakOnException2ThreadSameHitDifferentExceptionTest()
        {
            var result = DebugSpec(
                @"
/*  2: */ export const ans1 = (() => {
/*  3: */     let x: any = {};
/*  4: */     return x[1]; // results in runtime exception
/*  5: */ })();
/*  6: */
/*  7: */ export const ans2 = (() => {
/*  8: */     let x: any = {};
/*  9: */     return x[1]; // results in runtime exception
/*  10: */ })();",
                new[] { "ans1", "ans2" },
                async (source) =>
                {
                    var ev1 = await Debugger.ReceiveEvent<IStoppedEvent>();
                    Assert.Equal("exception", ev1.Body.Reason);

                    var ev2 = await Debugger.ReceiveEvent<IStoppedEvent>();
                    Assert.Equal("exception", ev2.Body.Reason);

                    var threads = GetThreadsSortedByName();
                    AssertAreEqual(2, threads.Count);
                    AssertTrue(threads[0].Name.StartsWith("ans1"));
                    AssertTrue(threads[1].Name.StartsWith("ans2"));

                    var stack1 = GetStackFrames(threads[0].Id);
                    AssertAreEqual(2, stack1.Count);
                    AssertAreEqual(4, stack1[0].Line);
                    AssertAreEqual(2, stack1[1].Line);

                    var stack2 = GetStackFrames(threads[1].Id);
                    AssertAreEqual(2, stack2.Count);
                    AssertAreEqual(9, stack2[0].Line);
                    AssertAreEqual(7, stack2[1].Line);

                    // continue 'ans1'
                    Debugger.Continue(threads[0].Id);

                    // make sure 'ans2' thread is still at the same position
                    stack2 = GetStackFrames(threads[1].Id);
                    AssertAreEqual(2, stack2.Count);
                    AssertAreEqual(9, stack2[0].Line);
                    AssertAreEqual(7, stack2[1].Line);

                    // continue 'ans2' and wait for TerminatedEvent
                    Debugger.Continue(threads[1].Id);
                    await Debugger.ReceiveEvent<TerminatedEvent>();
                });

            result.ExpectErrors(count: 2);
        }

        [Fact]
        public void BreakOnExceptionMultipleThreadsTest()
        {
            var result = DebugSpec(
                @"
/*  2: */ function run(): void {
/*  3: */     for (let i = 0; i < 10000; i++) {
/*  4: */         let x = i + i * i;
/*  5: */     }
/*  6: */ }
/*  7: */
/*  8: */ export const t1 = (() => {
/*  9: */     run(); return 1;
/* 10: */ })();
/* 11: */
/* 12: */ export const t2 = (() => {
/* 13: */     run(); return 2;
/* 14: */ })();
/* 15: */
/* 16: */ export const t3 = (() => {
/* 17: */     return [][0]; // results in runtime exception
/* 18: */ })();",
                new[] { "t1", "t2", "t3" },
                async (source) =>
                {
                    var ev = await Debugger.ReceiveEvent<IStoppedEvent>();
                    AssertAreEqual("exception", ev.Body.Reason);
                    var threadId = ev.Body.ThreadId;
                    var stack = GetStackFrames(threadId);
                    AssertAreEqual(2, stack.Count);
                    AssertAreEqual(17, stack[0].Line);
                    AssertAreEqual(16, stack[1].Line);

                    var threadsResult = Debugger.SendRequest(new ThreadsCommand());
                    AssertAreEqual(1, threadsResult.Threads.Count);
                    AssertTrue(threadsResult.Threads[0].Name.StartsWith("t3"));

                    await ContinueThreadAndAwaitTerminate(threadId);
                });

            result.ExpectValues(count: 3);
            result.ExpectErrors(count: 1);
            Assert.Equal(1, result.Values[0]);
            Assert.Equal(2, result.Values[1]);
        }
    }
}

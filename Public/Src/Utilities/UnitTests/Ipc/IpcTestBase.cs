// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Ipc
{
    public abstract class IpcTestBase : XunitBuildXLTest
    {
        private ITestOutputHelper Output { get; }

        private Exception m_unobservedTaskException = null;

        public IpcTestBase(ITestOutputHelper output)
            : base(null)
        { 
            Output = output;
            TaskScheduler.UnobservedTaskException += CatchUnobservedExceptions;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            TaskScheduler.UnobservedTaskException -= CatchUnobservedExceptions;
            if (m_unobservedTaskException != null)
            {
                XAssert.Fail("A task exception was not observed: " + m_unobservedTaskException.ToString());
            }
        }

        private void CatchUnobservedExceptions(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Volatile.Write(ref m_unobservedTaskException, e.Exception);
            e.SetObserved();
        }

        protected ILogger VerboseLogger(string testName)
        {
            return new LambdaLogger((level, format, args) =>
            {
                try
                {
                    string formatted = testName + " :: " + LoggerExtensions.Format(level, format, args);
                    Output.WriteLine(formatted);
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch
                {
                    // Couldn't care less if logging fails during a test execution.
                    // The reason why it can fail is this:
                    //   - there exist many "fire-and-forget" tasks in these tests
                    //   - since they are forgotten by the tests, they can attempt
                    //     to use this logger even after the test has been disposed
                    //   - when that happens, 'Output.WriteLine' throws.
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            });
        }

        protected IClientConfig ClientConfigWithLogger(string testName)
        {
            return new ClientConfig()
            {
                Logger = VerboseLogger(testName),
                MaxConnectRetries = 3,
                ConnectRetryDelay = TimeSpan.FromSeconds(1)
            };
        }

        protected IServerConfig ServerConfigWithLogger(string testName)
        {
            return new ServerConfig()
            {
                Logger = VerboseLogger(testName)
            };
        }

        protected static IIpcOperationExecutor EchoingExecutor => new LambdaIpcOperationExecutor((op) => IpcResult.Success(op.Payload));

        protected static IIpcOperationExecutor CrashingExecutor => new LambdaIpcOperationExecutor(new Func<IIpcOperation, IIpcResult>((op) =>
        {
            throw new Exception(op.Payload);
        }));

        protected static void WaitServerDone(IServer server)
        {
            server.Completion.Wait();
        }

        protected static IIpcResult SendWithTimeout(IClient client, IIpcOperation op)
        {
            var sendTask = client.Send(op);
            return sendTask.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Returns a cross product of 2 tuples.
        /// </summary>
        protected static IEnumerable<object[]> CrossProduct<T1, T2>(IEnumerable<T1> lhsTuple, IEnumerable<T2> rhsTuple)
        {
            foreach (var lhsAtom in lhsTuple)
            {
                foreach (var rhsAtom in rhsTuple)
                {
                    yield return new object[] { lhsAtom, rhsAtom };
                }
            }
        }

        /// <summary>
        /// Returns a cross product of 2 relations.
        /// </summary>
        protected static IEnumerable<object[]> CrossProduct<T1, T2>(IEnumerable<T1[]> lhsRelation, IEnumerable<T1[]> rhsRelation)
        {
            foreach (var lhsTuple in lhsRelation)
            {
                foreach (var rhsTuple in rhsRelation)
                {
                    var crossProduct = new object[lhsTuple.Length + rhsTuple.Length];
                    Array.Copy(lhsTuple, 0, crossProduct, 0, lhsTuple.Length);
                    Array.Copy(rhsTuple, 0, crossProduct, lhsTuple.Length, rhsTuple.Length);
                    yield return crossProduct;
                }
            }
        }
    }
}

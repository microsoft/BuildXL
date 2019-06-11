// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using BuildXL.Native.Streams.Windows;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Xunit.Abstractions;

namespace Test.BuildXL.TestUtilities.Xunit
{
    /// <summary>
    /// Base class that sends all events to the console; tremendously useful to diagnose remote test run failures
    /// </summary>
    /// <remarks>
    /// No two tests inheriting from this class may be run concurrently in the same process
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
        Justification = "Test follow different pattern with Initialize and Cleanup.")]
    public abstract class XunitBuildXLTest : BuildXLTestBase, IDisposable
    {
        private static readonly Lazy<IKextConnection> s_sandboxedKextConnection =  new Lazy<IKextConnection>(() =>
            OperatingSystemHelper.IsUnixOS
                ? new KextConnection(
                    skipDisposingForTests: true, 
                    config: new KextConnection.Config
                    {
                        MeasureCpuTimes = true,
                        FailureCallback = (status, description) =>
                        {
                            XAssert.Fail($"Kernel extension failed.  Status: {status}.  Description: {description}");
                        }
                    })
                : null);

        /// <summary>
        /// Returns a static kernel connection object. Unit tests would spam the kernel extension if they need sandboxing, so we
        /// tunnel all requests through the same object to keep kernel memory and CPU utilization low. On Windows machines this
        /// always returns null and causes no overhead for testing.
        /// </summary>
        public static IKextConnection GetSandboxedKextConnection()
        {
            return s_sandboxedKextConnection.Value;
        }

        /// <summary>
        /// Whether all diagnostic level messages should be captured to the console. Defaults to true.
        /// </summary>
        protected bool CaptureAllDiagnosticMessages = true;

        /// <summary>
        /// Please use the overload that takes a <see cref="ITestOutputHelper"/>
        /// </summary>
        protected XunitBuildXLTest()
            : this(null)
        {
            throw new NotImplementedException("Call the other constructor please! This ensures output is attributed to the appropriate test in test logging.");
        }

        /// <nodoc/>
        protected XunitBuildXLTest(ITestOutputHelper output)
        {
            // In xUnit there is no way to get the method name, but the classname is at least a bit more helpful than nothing.
            var fullyQualifiedTestName = GetType().FullName;
            if (output != null)
            {
                m_eventListener = new TestEventListener(
                    Events.Log,
                    fullyQualifiedTestName,
                    captureAllDiagnosticMessages: CaptureAllDiagnosticMessages,
                    logAction: (s) => output.WriteLine(s));
            }
            else
            {
                m_eventListener = new TestEventListener(Events.Log, fullyQualifiedTestName, captureAllDiagnosticMessages: true);
            }

            RegisterEventSource(global::BuildXL.Tracing.ETWLogger.Log);
            m_eventListener.EnableTaskDiagnostics(global::BuildXL.Tracing.ETWLogger.Tasks.CommonInfrastructure);

            if (!OperatingSystemHelper.IsUnixOS)
            {
                m_ioCompletionTraceHook = IOCompletionManager.Instance.StartTracingCompletion();
            }
        }

        /// <summary>
        /// Value returned by <see cref="DiscoverCurrentlyExecutingXunitTestMethodFQN"/> when it cannot discover 
        /// the currently executing XUnit method.
        /// </summary>
        protected const string UnknownXunitMethod = "<unknown>";

        /// <summary>
        /// Tries to discover the name of the currently executing XUnit method via reflection.
        /// </summary>
        protected string DiscoverCurrentlyExecutingXunitTestMethodFQN()
        {
            StackFrame testMethodFrame = new StackTrace()
                .GetFrames()
                .LastOrDefault(f => f.GetMethod().Module.Assembly == Assembly.GetAssembly(GetType()));

            if (testMethodFrame == null)
            {
                return UnknownXunitMethod;
            }

            return $"{testMethodFrame.GetMethod().DeclaringType.FullName}.{testMethodFrame.GetMethod().Name}";
        }

        /// <summary>
        /// Uses <see cref="PathGeneratorUtilities.GetAbsolutePath(string, string[])"/> to compose a platform-independent
        /// absolute path from a drive letter (<paramref name="drive"/>) a list of directories (<paramref name="dirList"/>).
        /// </summary>
        public static string A(string drive, params string[] dirList)
        {
            return PathGeneratorUtilities.GetAbsolutePath(drive, dirList);
        }

        /// <summary>
        /// Uses <see cref="PathGeneratorUtilities.GetAbsolutePath(string[])"/> to compose a
        /// platform-independent absolute path from a list of directories (<paramref name="dirList"/>).
        /// </summary>
        public static string A(params string[] dirList)
        {
            return PathGeneratorUtilities.GetAbsolutePath(dirList);
        }

        /// <summary>
        /// Takes a unix-style path (e.g., /c/Program Files/Visual Studio).  If running
        /// on a unix-like OS returns it; otherwise rewrites it to Windows-style (e.g., C:\Program Files\Visual Studio).
        /// </summary>
        public static string X(string unixPath, bool omitDriveLetter = false)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                if (omitDriveLetter)
                {
                    // Omit Windows type driver letter format e.g. /c
                    return unixPath.Substring(2);
                }
                
                return unixPath;
            }

            var splits = unixPath.Split('/').Where(a => !string.IsNullOrEmpty(a)).ToArray();
            return unixPath.StartsWith("/")
                ? A(splits)
                : R(splits);
        }

        /// <summary>
        /// Uses <see cref="PathGeneratorUtilities.GetRelativePath(string[])"/> to compose a
        /// platform-independent relative path a list of directories (<paramref name="dirList"/>).
        /// </summary>
        public static string R(params string[] dirList)
        {
            return PathGeneratorUtilities.GetRelativePath(dirList);
        }

        /// <inheritdoc/>
        protected override void AssertAreEqual(int expected, int actual, string format, params object[] args)
        {
            XAssert.AreEqual(expected, actual, format, args);
        }

        /// <inheritdoc/>
        protected override void AssertAreEqual(string expected, string actual, string format, params object[] args)
        {
            XAssert.AreEqual(expected, actual, format, args);
        }

        /// <inheritdoc/>
        protected override void AssertTrue(bool condition, string format, params object[] args)
        {
            XAssert.IsTrue(condition, format, args);
        }

        /// <summary>
        ///     1. creates a server
        ///     2. starts the server
        ///     3. invokes 'testAction'
        ///     4. shuts down the server
        ///     5. waits for the server to complete.
        /// </summary>
        protected static void WithIpcServer(IIpcProvider provider, IIpcOperationExecutor executor, IServerConfig config, Action<IIpcMoniker, IServer> testAction)
        {
            WithIpcServer(
                provider,
                executor,
                config,
                (moniker, server) =>
                {
                    testAction(moniker, server);
                    return Task.FromResult(1);
                }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Async version of <see cref="WithIpcServer(IIpcProvider, IIpcOperationExecutor, IServerConfig, Action{IIpcMoniker, IServer})"/>
        /// </summary>
        protected static async Task WithIpcServer(IIpcProvider provider, IIpcOperationExecutor executor, IServerConfig config, Func<IIpcMoniker, IServer, Task> testAction)
        {
            var moniker = provider.CreateNewMoniker();
            var server = provider.GetServer(provider.RenderConnectionString(moniker), config);
            server.Start(executor);
            try
            {
                await testAction(moniker, server);
            }
            finally
            {
                server.RequestStop();
                await server.Completion;
                server.Dispose();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <nodoc/>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    ValidateWarningsAndErrors();

                    if (m_ioCompletionTraceHook != null)
                    {
                        m_ioCompletionTraceHook.AssertTracedIOCompleted();
                    }
                }
                finally
                {
                    if (m_eventListener != null)
                    {
                        m_eventListener.Dispose();
                        m_eventListener = null;
                    }

                    if (m_ioCompletionTraceHook != null)
                    {
                        m_ioCompletionTraceHook.Dispose();
                    }
                }
            }
        }
    }
}

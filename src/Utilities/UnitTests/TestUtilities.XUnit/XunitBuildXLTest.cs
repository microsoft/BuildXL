// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Native.Streams.Windows;
using BuildXL.Storage;
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
        private static Lazy<ISandboxedKextConnection> s_sandboxedKextConnection =  new Lazy<ISandboxedKextConnection>(() => 
            OperatingSystemHelper.IsUnixOS ? new SandboxedKextConnection(1, skipDisposingForTests: true) : null);

        /// <summary>
        /// Returns a static kernel connection object. Unit tests would spam the kernel extension if they need sandboxing, so we
        /// tunnel all requests through the same object to keep kernel memory and CPU utilization low. On Windows machines this
        /// always returns null and causes no overhead for testing.
        /// </summary>
        /// <returns></returns>
        public static ISandboxedKextConnection GetSandboxedKextConnection()
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
            if (!OperatingSystemHelper.IsUnixOS)
            {
                m_ioCompletionTraceHook = IOCompletionManager.Instance.StartTracingCompletion();
            }
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
        public static string X(string unixPath)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
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
        protected override void AssertAreEqual(long expected, long actual, string format, params object[] args)
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
                    if (m_ioCompletionTraceHook != null)
                    {
                        m_ioCompletionTraceHook.AssertTracedIOCompleted();
                    }
                }
                finally
                {
                    if (m_ioCompletionTraceHook != null)
                    {
                        m_ioCompletionTraceHook.Dispose();
                    }
                }
            }
        }
    }
}

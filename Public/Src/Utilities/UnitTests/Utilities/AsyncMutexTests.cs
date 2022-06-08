// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.Executables.AsyncMutexClient.Program;

namespace Test.BuildXL.Utilities
{
    public class AsyncMutexTests : TemporaryStorageTestBase
    {
        private readonly string m_asynMutexClient;

        public AsyncMutexTests(ITestOutputHelper output) : base(output) 
        {
            m_asynMutexClient = Path.Combine(TestDeploymentDir, OperatingSystemHelper.IsUnixOS
                    ? "Test.BuildXL.Executables.AsyncMutexClient"
                    : "Test.BuildXL.Executables.AsyncMutexClient.exe");
        }

        [Fact]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer04:Fire-and-forget async call inside a using block", Justification = "Tasks are awaited later")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:Long-running or blocking operations inside an async method", Justification = "Not available on net472")]
        public async Task TaskValidationFlowAsync()
        {
            var outputFile = Path.Combine(TemporaryDirectory, "test.txt");
            using (var mutex = new AsyncMutex())
            {
                // Create two tasks that write to the same file
                var task1 = WriteLineWithLockAsync(mutex, outputFile);
                var task2 = WriteLineWithLockAsync(mutex, outputFile);

                await Task.WhenAll(task1, task2);

                // Check both tasks actually ran
                XAssert.ArrayEqual(File.ReadAllBytes(outputFile), new byte[] { 0, 1, 2, 0, 1, 2 });
            }
        }

        [Fact]
        public void ReleaseNotAcquiredThrows()
        {
            using (var mutex = new AsyncMutex())
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    return;
                }

                XAssert.IsTrue(false, "We shouldn't reach this line");
            }
        }

        [Fact]
        public void ReleaseWhenNotAcquiredOutOfProcThrows()
        {
            var result = ReleaseLockOutOfProc($"Mutex{Guid.NewGuid()}");
            XAssert.AreEqual(1, result);
        }

        [Fact]
        public void ReleaseWhenNotOwnedOutOfProcThrows()
        {
            var mutexName = $"Mutex{Guid.NewGuid()}";
            var acquireResult = AcquireLockOutOfProc(mutexName);
            XAssert.AreEqual(0, acquireResult);
            var releaseResult = ReleaseLockOutOfProc(mutexName);
            XAssert.AreEqual(1, releaseResult);
        }

        [Fact]
        public async Task AcquiredAndReleaseAsyncDoesNotThrow()
        {
            using (var mutex = new AsyncMutex())
            {
                bool exceptionOccurred = false;

                await mutex.WaitOneAsync(CancellationToken.None);

                // The release will run on an async continuation
                try
                {
                    mutex.ReleaseMutex();
                }
                catch(ApplicationException)
                {
                    exceptionOccurred = true;
                }

                XAssert.IsFalse(exceptionOccurred);
            }
        }

        [Fact]
        public void AcquireAndReleaseOutOfProc()
        {
            var mutexName = $"Mutex{Guid.NewGuid()}";
            // Not really testing concurrency here, but the fact that acquiring and releasing works fine out of proc
            var result1 = AcquireAndReleaseLockOutOfProc(mutexName);
            var result2 = AcquireAndReleaseLockOutOfProc(mutexName);
            XAssert.AreEqual(0, result1);
            XAssert.AreEqual(0, result2);
        }

        [Fact]
        public void CancellationIsHonored()
        {
            var cancelled = false;
            using (var cancellationSource = new CancellationTokenSource())
            using (var mutex = new AsyncMutex())
            {

                // Acquire the mutex
                mutex.WaitOneAsync(CancellationToken.None).GetAwaiter().GetResult();

                var acquireThread = new Thread(() =>
                {
                    try
                    {
                        // Acquire the same mutex on a different thread. This should make the thread wait until cancelled
                        mutex.WaitOneAsync(cancellationSource.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                });
                acquireThread.Start();

                cancellationSource.Cancel();

                acquireThread.Join();
            }

            XAssert.IsTrue(cancelled);
        }

        public static async Task WriteLineWithLockAsync(AsyncMutex mutex, string outputFile)
        {
            await mutex.WaitOneAsync(CancellationToken.None);

            try
            {
                // Append to the file with FileShare.None as a way to guarantee single access
                using (var s = new FileStream(outputFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                {
                    s.Seek(0, SeekOrigin.End);
                    await s.WriteAsync(new byte[] { 0, 1, 2 }, 0, 3);
                }
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        private int RunAsyncMutexClient(string mutexName, AsyncMutexClientAction action)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = m_asynMutexClient,
                Arguments = $"{mutexName} {action}",
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false,
            };

            var process = Process.Start(processInfo);
            process.WaitForExit();

            return process.ExitCode;
        }

        private int AcquireLockOutOfProc(string mutexName)
        {
            return RunAsyncMutexClient(mutexName, AsyncMutexClientAction.Acquire);
        }

        private int ReleaseLockOutOfProc(string mutexName)
        {
            return RunAsyncMutexClient(mutexName, AsyncMutexClientAction.Release);
        }

        private int AcquireAndReleaseLockOutOfProc(string mutexName)
        {
            return RunAsyncMutexClient(mutexName, AsyncMutexClientAction.AcquireAndRelease);
        }
    }
}

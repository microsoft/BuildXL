// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    public sealed partial class SchedulerTest
    {
        [Fact]
        public void LockTest_GlobalLock_PipLockMutallyExclusive()
        {
            Setup();
            var lockManager = new LockManager();

            // Global lock and pip lock are mutually exclusive
            TestMutuallyExclusive(() => lockManager.AcquireGlobalExclusiveLock(), () => lockManager.AcquireLock(new PipId(20)));
        }

        [Fact]
        public void LockTest_GlobalLock_PathLockMutallyExclusive()
        {
            Setup();
            var lockManager = new LockManager();

            // Global lock and path lock are mutually exclusive
            var copyFile = CreateCopyFile(CreateSourceFile(), CreateOutputFileArtifact());
            TestMutuallyExclusive(() => lockManager.AcquireGlobalExclusiveLock(), () => lockManager.AcquirePathAccessLock(copyFile));
        }

        [Fact]
        public void LockTest_CommonPipLockMutallyExclusive()
        {
            Setup();
            var lockManager = new LockManager();

            // Single pip contention
            TestMutuallyExclusive(() => lockManager.AcquireLock(new PipId(20)), () => lockManager.AcquireLock(new PipId(20)));

            // Single pip contention with one pair pip lock
            TestMutuallyExclusive(() => lockManager.AcquireLocks(new PipId(20), new PipId(21)), () => lockManager.AcquireLock(new PipId(20)));

            // Pair pip contention reverse order
            TestMutuallyExclusive(() => lockManager.AcquireLocks(new PipId(20), new PipId(21)), () => lockManager.AcquireLocks(new PipId(21), new PipId(20)));
        }

        [Fact]
        public void LockTest_WriteSamePathMutallyExclusive()
        {
            Setup();
            var lockManager = new LockManager();

            // Test writes to same path are exclusive
            var outputFile = CreateOutputFileArtifact();

            // Create pips which write to the same location
            var copyFile1 = CreateCopyFile(CreateSourceFile(), outputFile);
            var copyFile2 = CreateCopyFile(CreateSourceFile(), outputFile);
            var copyFile3 = CreateCopyFile(CreateSourceFile(), outputFile);
            var process = CreateProcess(new[] { CreateSourceFile() }, new[] { outputFile });

            TestMutuallyExclusive(
                () => lockManager.AcquirePathAccessLock(copyFile1),
                () => lockManager.AcquirePathAccessLock(copyFile2));

            TestMutuallyExclusive(
                () => lockManager.AcquirePathAccessLock(process),
                () => lockManager.AcquirePathAccessLock(copyFile3));
        }

        [Fact]
        public void LockTest_WriteReadSamePathMutallyExclusive()
        {
            Setup();
            var lockManager = new LockManager();

            // Test read and write to same path are mutually exclusive
            var root = CreateUniqueObjPath("sealed");
            var outputFile = CreateOutputFileArtifact(root.ToString(Context.PathTable));
            
            var seal = CreateSealDirectory(root, SealDirectoryKind.Partial, CreateSourceFile(), outputFile);
            var process = CreateProcess(new[] { CreateSourceFile() }, new[] { outputFile });

            TestMutuallyExclusive(
                () => lockManager.AcquirePathAccessLock(process),
                () => lockManager.AcquirePathAccessLock(seal));
        }

        [Fact]
        public void LockTest_ReadSamePathNotMutallyExclusive()
        {
            Setup();
            var lockManager = new LockManager();

            // Test reads are NOT mutually exclusive
            var outputFileInput = CreateOutputFileArtifact();

            var process1 = CreateProcess(new[] { outputFileInput }, new[] { CreateOutputFileArtifact() });
            var process2 = CreateProcess(new[] { outputFileInput }, new[] { CreateOutputFileArtifact() });

            TestMutuallyExclusive(
                () => lockManager.AcquirePathAccessLock(process1),
                () => lockManager.AcquirePathAccessLock(process2),
                assertExclusive: false);
        }

        [Fact]
        public void LockTest_ReadPathsWithInnerExclusiveMutallyExclusive()
        {
            Setup();
            var lockManager = new LockManager();

            // Test reads are NOT mutually exclusive
            var outputFileInput = CreateOutputFileArtifact();

            var process1 = CreateProcess(new[] { outputFileInput }, new[] { CreateOutputFileArtifact() });
            var process2 = CreateProcess(new[] { outputFileInput }, new[] { CreateOutputFileArtifact() });

            using (var sharedAccessLock1 = lockManager.AcquirePathAccessLock(process1))
            using (var sharedAccessLock2 = lockManager.AcquirePathAccessLock(process2))
            {
                // Test that two group locks can't acquire the inner exclusive lock for the same path
                // at the same time
                TestMutuallyExclusive(
                    () => sharedAccessLock1.AcquirePathInnerExclusiveLock(outputFileInput.Path),
                    () => sharedAccessLock2.AcquirePathInnerExclusiveLock(outputFileInput.Path));
            }
        }

        [Fact]
        public void LockTest_PathPipLockNotMutallyExclusive()
        {
            Setup();
            var lockManager = new LockManager();

            var process = CreateProcess(new[] { CreateSourceFile() }, new[] { CreateOutputFileArtifact() });

            // Test pip locks and path locks are NOT mutually exclusive
            TestMutuallyExclusive(
                () => lockManager.AcquirePathAccessLock(process),
                () => lockManager.AcquireLock(new PipId(30)),
                assertExclusive: false);
        }

        /// <summary>
        /// Ensures that two locks are mutually exclusive by trying to acquire one lock (on another thread) while the other lock is held
        /// and waiting the specified amount of time
        /// </summary>
        private void TestMutuallyExclusive<TLock1, TLock2>(Func<TLock1> lockCreator1, Func<TLock2> lockCreator2, int waitTimeInMilliseconds = 500, bool assertExclusive = true)
            where TLock1 : IDisposable
            where TLock2 : IDisposable
        {
            // Try with first lock held
            TestMutuallyExclusiveHelper(lockCreator1, lockCreator2, 1, 2, waitTimeInMilliseconds, assertExclusive);

            // Now try with second lock held
            TestMutuallyExclusiveHelper(lockCreator2, lockCreator1, 2, 1, waitTimeInMilliseconds, assertExclusive);

            if (assertExclusive)
            {
                Task lockCreator1StartedEvent;
                Task lockCreator2StartedEvent;

                TaskSourceSlim<int[]> accessCheckTaskProvider = TaskSourceSlim.Create<int[]>();
                var loop1Completion = TryAcquireLoop(lockCreator1, accessCheckTaskProvider.Task, out lockCreator1StartedEvent);
                var loop2Completion = TryAcquireLoop(lockCreator2, accessCheckTaskProvider.Task, out lockCreator2StartedEvent);

                // Wait for task bodies to be entered
                lockCreator1StartedEvent.Wait();
                lockCreator2StartedEvent.Wait();

                // Start the loops and provide the access check array
                accessCheckTaskProvider.SetResult(new int[1]);

                // Wait for the loops to complete
                loop1Completion.GetAwaiter().GetResult();
                loop2Completion.GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Ensures that two locks are mutually exclusive by trying to acquire one lock (on another thread) while the other lock is held
        /// and waiting the specified amount of time
        /// </summary>
        private void TestMutuallyExclusiveHelper<TLock1, TLock2>(Func<TLock1> lockCreator1, Func<TLock2> lockCreator2, int lockNumber1, int lockNumber2, int waitTimeInMilliseconds, bool assertExclusive)
            where TLock1 : IDisposable
            where TLock2 : IDisposable
        {
            TaskSourceSlim<bool> lockCreator2ExecutingEvent = TaskSourceSlim.Create<bool>();
            bool[] lock2Entered = new bool[1];

            Task lockCreator2CompletionTask;
            using (lockCreator1())
            {
                lockCreator2CompletionTask = Task.Run(() =>
                {
                    lockCreator2ExecutingEvent.SetResult(true);
                    using (lockCreator2())
                    {
                    }

                    lock2Entered[0] = true;
                });

                // Wait for the task to start executing first so the next wait timeout
                // doesn't vary depending on when the task scheduler decides to execute the task
                lockCreator2ExecutingEvent.Task.Wait();

                if (assertExclusive)
                {
                    XAssert.IsFalse(lockCreator2CompletionTask.Wait(waitTimeInMilliseconds), string.Format(CultureInfo.InvariantCulture, "SHOULD NOT be able to enter lock {0} while lock {1} is held", lockNumber1, lockNumber2));
                }
                else
                {
                    XAssert.IsTrue(lockCreator2CompletionTask.Wait(waitTimeInMilliseconds), string.Format(CultureInfo.InvariantCulture, "SHOULD be able to enter lock {0} while lock {1} is held", lockNumber1, lockNumber2));
                }
            }

            XAssert.IsTrue(lockCreator2CompletionTask.Wait(waitTimeInMilliseconds) || lock2Entered[0], "Should be able to enter lock 2 after lock 1 is released");
        }

        /// <summary>
        /// Attempts to acquire lock in a tight loop ensuring that it has exclusive access to an arrays
        /// </summary>
        private Task TryAcquireLoop<TLock>(Func<TLock> lockCreator, Task<int[]> accessCheckTask, out Task startedEvent)
            where TLock : IDisposable
        {
            TaskSourceSlim<bool> startedEventCompletion = TaskSourceSlim.Create<bool>();
            startedEvent = startedEventCompletion.Task;
            return Task.Run(() =>
                {
                    startedEventCompletion.SetResult(true);

                    // Wait on this task to ensure the loops start around the same time
                    int[] accessCheck = accessCheckTask.Result;
                    for (int i = 0; i < 10000; i++)
                    {
                        using (lockCreator())
                        {
                            // Each operation should succeed with the anticipated value
                            // if we have exclusive access
                            XAssert.AreEqual(1, Interlocked.Increment(ref accessCheck[0]));
                            XAssert.AreEqual(0, Interlocked.Decrement(ref accessCheck[0]));
                        }
                    }
                });
        }
    }
}

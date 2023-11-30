// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Named semaphore tests.
    /// </summary>
    /// <remarks>
    /// Named semaphores not implemented on macOS.
    /// </remarks>
    [TestClassIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
    public class NamedSemaphoreTest : XunitBuildXLTest
    {
        public NamedSemaphoreTest(ITestOutputHelper output)
        : base(output)
        {
        }


        [TheoryIfSupported(requiresLinuxBasedOperatingSystem: true)]
        [InlineData("sem")]
        [InlineData("/sem/")]
        public void TestChecksInvalidName(string name)
        {
            var maybeSemaphore = LinuxNamedSemaphore.CreateNew(name, 1);
            Assert.False(maybeSemaphore.Succeeded);
        }

        [FactIfSupported(requiresLinuxBasedOperatingSystem: true)]
        public void TestAlreadyExists()
        {
            var semaphoreName = "/sem";

            var sem1 = SemaphoreFactory.CreateNew(semaphoreName, 1, 2);
            Assert.True(sem1.Succeeded);

            // creating a semaphore with the same name is not allowed while the other one is still in use
            var sem2 = SemaphoreFactory.CreateNew(semaphoreName, 1, 2);
            Assert.False(sem2.Succeeded);

            // after disposing, another semaphore with the same name can be created
            sem1.Result.Dispose();
            var sem3 = SemaphoreFactory.CreateNew(semaphoreName, 1, 2);
            Assert.True(sem3.Succeeded);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(20)]
        public void TestCriticalSection(int numThreads)
        {
            var semaphoreName = (OperatingSystemHelper.IsWindowsOS ? "" : "/") + Guid.NewGuid().ToString().Replace("-", "");

            var sem = SemaphoreFactory.CreateNew(semaphoreName, 1, 20);
            Assert.True(sem.Succeeded);
            int val = 0;
            var threads = Enumerable
                .Range(0, numThreads)
                .Select(_ => new Thread(() =>
                {
                    sem.Result.WaitOne(-1);
                    int inc = val + 1;
                    Thread.Sleep(2);
                    val = inc;
                    sem.Result.Release();
                }))
                .ToArray();
            Array.ForEach(threads, t => t.Start());
            Array.ForEach(threads, t => t.Join());
            Assert.True(numThreads == val, $"Expected {numThreads} threads, got {val}");

            sem.Result.Dispose();
        }

    }
}

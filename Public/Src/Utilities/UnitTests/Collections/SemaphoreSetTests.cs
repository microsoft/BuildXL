// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class SemaphoreSetTests : XunitBuildXLTest
    {
        public SemaphoreSetTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestSemaphoreSet()
        {
            SemaphoreSet<int> semaphores = new SemaphoreSet<int>();
            int[] limits = new int[] { 1, 2, 3, 10 };
            int originalLimitsLength = limits.Length;
            for (int i = 0; i < limits.Length; i++)
            {
                int semaphoreIndex = semaphores.CreateSemaphore(i, limits[i]);
                XAssert.AreEqual(i, semaphoreIndex);
            }

            for (int i = 0; i < limits.Length; i++)
            {
                var limit = limits[i];
                for (int usage = 1; usage <= limit + 1; usage++)
                {
                    int semaphoreIndex = semaphores.CreateSemaphore(i, limits[i]);
                    XAssert.AreEqual(i, semaphoreIndex);
                    var semaphoreIncrements = new int[i + 1];
                    semaphoreIncrements[i] = 1;
                    bool acquired = semaphores.TryAcquireResources(ItemResources.Create(semaphoreIncrements));
                    bool expectAcquired = usage <= limit;
                    XAssert.AreEqual(expectAcquired, acquired);
                }
            }

            // Now verify semaphores can be acquired in the copy
            var copiedSemaphores = semaphores.CreateSharingCopy();
            for (int i = 0; i < limits.Length; i++)
            {
                var limit = limits[i];
                for (int usage = 1; usage <= limit + 1; usage++)
                {
                    var semaphoreIncrements = new int[i + 1];
                    semaphoreIncrements[i] = 1;
                    bool acquired = copiedSemaphores.TryAcquireResources(ItemResources.Create(semaphoreIncrements));
                    bool expectAcquired = usage <= limit;
                    XAssert.AreEqual(expectAcquired, acquired);
                }
            }

            limits = limits.Concat(new int[] { 6, 8 }).ToArray();

            // Create new semaphores in the copy and verify that semaphores can be used in original
            for (int i = 0; i < limits.Length; i++)
            {
                int semaphoreIndex = copiedSemaphores.CreateSemaphore(i, limits[i]);
                XAssert.AreEqual(i, semaphoreIndex);
            }

            for (int i = originalLimitsLength; i < limits.Length; i++)
            {
                var limit = limits[i];
                for (int usage = 1; usage <= limit + 1; usage++)
                {
                    var semaphoreIncrements = new int[i + 1];
                    semaphoreIncrements[i] = 1;
                    bool acquired = semaphores.TryAcquireResources(ItemResources.Create(semaphoreIncrements));
                    bool expectAcquired = usage <= limit;
                    XAssert.AreEqual(expectAcquired, acquired);
                }
            }

            // Now verify new semaphores can be acquired in the copy
            for (int i = originalLimitsLength; i < limits.Length; i++)
            {
                var limit = limits[i];
                for (int usage = 1; usage <= limit + 1; usage++)
                {
                    var semaphoreIncrements = new int[i + 1];
                    semaphoreIncrements[i] = 1;
                    bool acquired = copiedSemaphores.TryAcquireResources(ItemResources.Create(semaphoreIncrements));
                    bool expectAcquired = usage <= limit;
                    XAssert.AreEqual(expectAcquired, acquired);
                }
            }

            // Now release the resources in the copy
            for (int i = 0; i < limits.Length; i++)
            {
                var limit = limits[i];
                for (int usage = 1; usage <= limit; usage++)
                {
                    var semaphoreIncrements = new int[i + 1];
                    semaphoreIncrements[i] = 1;
                    copiedSemaphores.ReleaseResources(ItemResources.Create(semaphoreIncrements));
                }
            }

            // Verify that resources still can't be acquired on original
            for (int i = 0; i < limits.Length; i++)
            {
                var semaphoreIncrements = new int[i + 1];
                semaphoreIncrements[i] = 1;
                bool acquired = semaphores.TryAcquireResources(ItemResources.Create(semaphoreIncrements));
                XAssert.IsFalse(acquired);
            }
        }

        [Fact]
        public void TestSemaphoreSet_MultipleSemaphoreAcquisition()
        {
            SemaphoreSet<int> semaphores = new SemaphoreSet<int>();

            int[] limits = new int[] { 1, 3, 7, 10 };
            for (int i = 0; i < limits.Length; i++)
            {
                int semaphoreIndex = semaphores.CreateSemaphore(i, limits[i]);
                XAssert.AreEqual(i, semaphoreIndex);
            }

            // Try acquiring more than on semaphore at once
            for (int i = 0; i < 4; i++)
            {
                bool acquired = semaphores.TryAcquireResources(ItemResources.Create(new int[] { 0, 1, 0, 1 }));
                bool expectAcquired = i != 3;
            }

            // Create a new copy with independent usages
            semaphores = semaphores.CreateSharingCopy();

            // Try acquiring more than on semaphore at once with count > 1
            for (int i = 0; i < 4; i++)
            {
                bool acquired = semaphores.TryAcquireResources(ItemResources.Create(new int[] { 0, 0, 2, 2 }));
                bool expectAcquired = i != 3;
            }
        }
    }
}

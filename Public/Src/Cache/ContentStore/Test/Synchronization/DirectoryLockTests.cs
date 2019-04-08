// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Synchronization
{
    public abstract class DirectoryLockTests : TestBase
    {
        protected DirectoryLockTests(Func<IAbsFileSystem> createFileSystem, ITestOutputHelper output)
            : base(createFileSystem, TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task LockAcquisitionResultContainsInformationAboutOtherProcess()
        {
            string componentName = "component1";
            var context = new Context(Logger);
            var timeout = TimeSpan.FromMilliseconds(100);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            using (var lock1 = new DirectoryLock(testDirectory.Path / "lock1", FileSystem, TimeSpan.FromMinutes(30), componentName))
            using (var lock2 = new DirectoryLock(testDirectory.Path / "lock1", FileSystem, timeout, componentName))
            {
                (await lock1.AcquireAsync(context)).LockAcquired.Should().BeTrue();
                var failure = await lock2.AcquireAsync(context);
                failure.LockAcquired.Should().BeFalse();
                failure.Timeout.Should().Be(timeout);

                // Fix for InMemoryFileSystem. Bug #1334691
                if (!BuildXL.Utilities.OperatingSystemHelper.IsUnixOS && FileSystem is PassThroughFileSystem)
                {
                    // Used to be a flaky check. Enable it because it is important.
                    failure.CompetingProcessId.HasValue.Should().BeTrue();
                    Assert.NotNull(failure.CompetingProcessName);
                }
            }
        }

        [Theory]
        [InlineData("dir1", "dir2", null, null)] // Different paths, null component
        [InlineData("dir1", "dir2", "TestComponent1", "TestComponent1")] // Different paths, same component
        [InlineData("dir1", "dir1", "TestComponent1", "TestComponent2")] // Same path, different components
        [InlineData("dir1", "dir1", "TestComponent1", null)] // Same path, one null component
        [InlineData("dir1", "dir2", "TestComponent1", "TestComponent2")] // Different paths, different components
        [InlineData("dir1", "dir2", "TestComponent1", null)] // Different paths, one null component
        public async Task DifferentNoBlocking(string directoryName1, string directoryName2, string componentName1, string componentName2)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            using (var lock1 = new DirectoryLock(testDirectory.Path / directoryName1, FileSystem, TimeSpan.FromMinutes(30), componentName1))
            using (var lock2 = new DirectoryLock(testDirectory.Path / directoryName2, FileSystem, TimeSpan.FromMinutes(30), componentName2))
            {
                (await lock1.AcquireAsync(context)).LockAcquired.Should().BeTrue();
                (await lock2.AcquireAsync(context)).LockAcquired.Should().BeTrue();
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("TestComponent1")]
        public async Task SameBlocks(string componentName)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            using (var lock1 = new DirectoryLock(testDirectory.Path / "dir1", FileSystem, TimeSpan.FromMinutes(30), componentName))
            using (var lock2 = new DirectoryLock(testDirectory.Path / "dir1", FileSystem, TimeSpan.FromMilliseconds(100), componentName))
            {
                (await lock1.AcquireAsync(context)).LockAcquired.Should().BeTrue();
                (await lock2.AcquireAsync(context)).LockAcquired.Should().BeFalse();
            }
        }

        [Trait("Category", "QTestSkip")] // Skipped
        [Theory(Skip = "TODO: Failing locally during conversion")]
        [InlineData(null)]
        [InlineData("TestComponent1")]
        public async Task SameAcquiresAfterDisposal(string componentName)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            using (var lock1 = new DirectoryLock(testDirectory.Path / "dir1", FileSystem, TimeSpan.FromMinutes(30), componentName))
            using (var lock2 = new DirectoryLock(testDirectory.Path / "dir1", FileSystem, TimeSpan.FromMilliseconds(100), componentName))
            {
                (await lock1.AcquireAsync(context)).LockAcquired.Should().BeTrue();
                lock1.Dispose();
                (await lock2.AcquireAsync(context)).LockAcquired.Should().BeTrue();
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("TestComponent1")]
        public async Task HoldingLockFileDoesNotWedge(string componentName)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            using (var lockFile = new DirectoryLockFile(FileSystem, testDirectory.Path / "dir1" / $"{componentName ?? string.Empty}.lock", TimeSpan.FromMilliseconds(100)))
            using (var lock1 = new DirectoryLock(testDirectory.Path / "dir1", FileSystem, TimeSpan.FromSeconds(1), componentName))
            using (var lock2 = new DirectoryLock(testDirectory.Path / "dir1", FileSystem, TimeSpan.FromSeconds(1), componentName))
            {
                (await lockFile.AcquireAsync(context, TimeSpan.FromMinutes(1))).LockAcquired.Should().BeTrue();
                (await lock1.AcquireAsync(context)).LockAcquired.Should().BeFalse();
                lockFile.Dispose();
                (await lock2.AcquireAsync(context)).LockAcquired.Should().BeTrue();
            }
        }

        [Theory(Skip = "Investigate why the test fails. Bug #1334667")]
        [InlineData(null)]
        [InlineData("TestComponent1")]
        public async Task SameObjectAcquiresMultipleTimes(string componentName)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            using (var lock1 = new DirectoryLock(testDirectory.Path / "dir1", FileSystem, TimeSpan.FromSeconds(1), componentName))
            {
                var failedToAcquire = false;
                var actionBlock = new ActionBlock<ValueUnit>(async _ =>
                {
                    if (!(await lock1.AcquireAsync(context)).LockAcquired)
                    {
                        failedToAcquire = true;
                    }
                });

                await actionBlock.PostAllAndComplete(Enumerable.Repeat(ValueUnit.Void, 5));
                failedToAcquire.Should().BeFalse();
            }
        }
    }
}

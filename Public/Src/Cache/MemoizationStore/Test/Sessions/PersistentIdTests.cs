// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class PersistentIdTests : TestBase
    {
        public PersistentIdTests()
            : base(() => new MemoryFileSystem(TestSystemClock.Instance), TestGlobal.Logger)
        {
        }

        [Fact]
        public void Persists()
        {
            TestPersistentId(FileSystem, idFilePath =>
            {
                var id = PersistentId.Load(FileSystem, idFilePath);
                PersistentId.Load(FileSystem, idFilePath).Should().Be(id);
            });
        }

        [Fact]
        public void CreatesNewOnNonexistent()
        {
            TestPersistentId(FileSystem, idFilePath =>
            {
                var id = PersistentId.Load(FileSystem, idFilePath);
                id.Should().NotBe(Guid.Empty);
                FileSystem.DeleteFile(idFilePath);
                var newId = PersistentId.Load(FileSystem, idFilePath);
                newId.Should().NotBe(Guid.Empty);
                newId.Should().NotBe(id);
            });
        }

        [Fact]
        public void CreatesNewOnCorrupted()
        {
            TestPersistentId(FileSystem, idFilePath =>
            {
                var id = PersistentId.Load(FileSystem, idFilePath);
                FileSystem.WriteAllBytes(idFilePath, new byte[] {});
                var newId = PersistentId.Load(FileSystem, idFilePath);
                newId.Should().NotBe(id);
            });
        }

        [Fact]
        public void CreatesNonSpecialValue()
        {
            TestPersistentId(FileSystem, idFilePath =>
            {
                var id = PersistentId.Load(FileSystem, idFilePath);
                id.Should().NotBe(CacheDeterminism.None.EffectiveGuid);
                id.Should().NotBe(CacheDeterminism.Tool.EffectiveGuid);
                id.Should().NotBe(CacheDeterminism.SinglePhaseNonDeterministic.EffectiveGuid);
            });
        }

        [Fact]
        public void CreatesDirectoryIfMissing()
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var missingDirectory = testDirectory.Path / "MissingDirectory";
                var id = PersistentId.Load(FileSystem, missingDirectory / "Cache.id");
                id.Should().NotBe(Guid.Empty);
                FileSystem.DirectoryExists(missingDirectory).Should().BeTrue();
            }
        }

        private void TestPersistentId(IAbsFileSystem fileSystem, Action<AbsolutePath> testAction)
        {
            using (var testDirectory = new DisposableDirectory(fileSystem))
            {
                var idFilePath = testDirectory.Path / "Cache.id";
                testAction(idFilePath);
            }
        }
    }
}

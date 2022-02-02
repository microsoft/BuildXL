// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Utilities;
using FluentAssertions;
using System.Text.Json;
using Xunit;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class CheckpointManagerTests
    {
        [Fact]
        public void RocksDbCorruptionRegexMatchesSlot1()
        {
            var error = @"RocksDbSharp.RocksDbException: Corruption: block checksum mismatch: expected 1142279158, got 647716707  in K:\dbs\Cache\ContentAddressableStore\LocationDb\Slot1/1089482.sst offset 69135151 size 19421748
   at RocksDbSharp.Native.rocksdb_open_for_read_only_column_families(IntPtr options, String name, Int32 num_column_families, String[] column_family_names, IntPtr[] column_family_options, IntPtr[] column_family_handles, Boolean error_if_log_file_exist)
   at RocksDbSharp.RocksDb.OpenReadOnly(DbOptions options, String path, ColumnFamilies columnFamilies, Boolean errIfLogFileExists)
   at BuildXL.Engine.Cache.KeyValueStores.RocksDbStore..ctor(RocksDbStoreConfiguration configuration) in \.\Public\Src\Utilities\KeyValueStore\RocksDb\RocksDbStore.cs:line 246
   at BuildXL.Engine.Cache.KeyValueStores.KeyValueStoreAccessor..ctor(RocksDbStoreConfiguration storeConfiguration, Int32 storeVersion, Action`1 failureHandler, Boolean createdNewStore, Action`1 invalidationHandler) in \.\Public\Src\Utilities\KeyValueStore\KeyValueStoreAccessor.cs:line 715
   at BuildXL.Engine.Cache.KeyValueStores.KeyValueStoreAccessor.OpenInternal(RocksDbStoreConfiguration storeConfiguration, Int32 storeVersion, Action`1 failureHandler, Boolean createNewStore, Action`1 invalidationHandler) in \.\Public\Src\Utilities\KeyValueStore\KeyValueStoreAccessor.cs:line 581";
            var expectedName = "1089482.sst";

            CorruptionRegexCheck(error, expectedName);
        }

        [Fact]
        public void RocksDbCorruptionRegexMatchesSlot2()
        {
            var error = @"RocksDbSharp.RocksDbException: Corruption: block checksum mismatch: expected 1142279158, got 647716707  in K:\dbs\Cache\ContentAddressableStore\LocationDb\Slot2/1482.sst offset 69135151 size 19421748
   at RocksDbSharp.Native.rocksdb_open_for_read_only_column_families(IntPtr options, String name, Int32 num_column_families, String[] column_family_names, IntPtr[] column_family_options, IntPtr[] column_family_handles, Boolean error_if_log_file_exist)
   at RocksDbSharp.RocksDb.OpenReadOnly(DbOptions options, String path, ColumnFamilies columnFamilies, Boolean errIfLogFileExists)
   at BuildXL.Engine.Cache.KeyValueStores.RocksDbStore..ctor(RocksDbStoreConfiguration configuration) in \.\Public\Src\Utilities\KeyValueStore\RocksDb\RocksDbStore.cs:line 246
   at BuildXL.Engine.Cache.KeyValueStores.KeyValueStoreAccessor..ctor(RocksDbStoreConfiguration storeConfiguration, Int32 storeVersion, Action`1 failureHandler, Boolean createdNewStore, Action`1 invalidationHandler) in \.\Public\Src\Utilities\KeyValueStore\KeyValueStoreAccessor.cs:line 715
   at BuildXL.Engine.Cache.KeyValueStores.KeyValueStoreAccessor.OpenInternal(RocksDbStoreConfiguration storeConfiguration, Int32 storeVersion, Action`1 failureHandler, Boolean createNewStore, Action`1 invalidationHandler) in \.\Public\Src\Utilities\KeyValueStore\KeyValueStoreAccessor.cs:line 581";
            var expectedName = "1482.sst";

            CorruptionRegexCheck(error, expectedName);
        }

        [Fact]
        public void RocksDbCorruptionRegexMatchesBackwardSlash()
        {
            var error = @"RocksDbSharp.RocksDbException: Corruption: block checksum mismatch: expected 1142279158, got 647716707  in K:\dbs\Cache\ContentAddressableStore\LocationDb\Slot2\1482.sst offset 69135151 size 19421748
   at RocksDbSharp.Native.rocksdb_open_for_read_only_column_families(IntPtr options, String name, Int32 num_column_families, String[] column_family_names, IntPtr[] column_family_options, IntPtr[] column_family_handles, Boolean error_if_log_file_exist)
   at RocksDbSharp.RocksDb.OpenReadOnly(DbOptions options, String path, ColumnFamilies columnFamilies, Boolean errIfLogFileExists)
   at BuildXL.Engine.Cache.KeyValueStores.RocksDbStore..ctor(RocksDbStoreConfiguration configuration) in \.\Public\Src\Utilities\KeyValueStore\RocksDb\RocksDbStore.cs:line 246
   at BuildXL.Engine.Cache.KeyValueStores.KeyValueStoreAccessor..ctor(RocksDbStoreConfiguration storeConfiguration, Int32 storeVersion, Action`1 failureHandler, Boolean createdNewStore, Action`1 invalidationHandler) in \.\Public\Src\Utilities\KeyValueStore\KeyValueStoreAccessor.cs:line 715
   at BuildXL.Engine.Cache.KeyValueStores.KeyValueStoreAccessor.OpenInternal(RocksDbStoreConfiguration storeConfiguration, Int32 storeVersion, Action`1 failureHandler, Boolean createNewStore, Action`1 invalidationHandler) in \.\Public\Src\Utilities\KeyValueStore\KeyValueStoreAccessor.cs:line 581";
            var expectedName = "1482.sst";

            CorruptionRegexCheck(error, expectedName);
        }

        [Fact]
        public void RocksDbCorruptionRegexMatchesBadTableMagicNumber()
        {
            var error = @"RocksDbSharp.RocksDbException: Corruption: Bad table magic number: expected 9863518390377041911, found 0 in K:\dbs\Cache\ContentAddressableStore\LocationDb\Slot2/1709103.sst
   at RocksDbSharp.Native.rocksdb_open_for_read_only_column_families(IntPtr options, String name, Int32 num_column_families, String[] column_family_names, IntPtr[] column_family_options, IntPtr[] column_family_handles, Boolean error_if_log_file_exist)
   at RocksDbSharp.RocksDb.OpenReadOnly(DbOptions options, String path, ColumnFamilies columnFamilies, Boolean errIfLogFileExists)
   at BuildXL.Engine.Cache.KeyValueStores.RocksDbStore..ctor(RocksDbStoreConfiguration configuration) in \.\Public\Src\Utilities\KeyValueStore\RocksDb\RocksDbStore.cs:line 246
   at BuildXL.Engine.Cache.KeyValueStores.KeyValueStoreAccessor..ctor(RocksDbStoreConfiguration storeConfiguration, Int32 storeVersion, Action`1 failureHandler, Boolean createdNewStore, Action`1 invalidationHandler) in \.\Public\Src\Utilities\KeyValueStore\KeyValueStoreAccessor.cs:line 715
   at BuildXL.Engine.Cache.KeyValueStores.KeyValueStoreAccessor.OpenInternal(RocksDbStoreConfiguration storeConfiguration, Int32 storeVersion, Action`1 failureHandler, Boolean createNewStore, Action`1 invalidationHandler) in \.\Public\Src\Utilities\KeyValueStore\KeyValueStoreAccessor.cs:line 581";
            var expectedName = "1709103.sst";

            CorruptionRegexCheck(error, expectedName);
        }

        private static void CorruptionRegexCheck(string error, string expectedName)
        {
            var match = CheckpointManager.RocksDbCorruptionRegex.Match(error);
            var name = match.Groups["name"];
            match.Success.Should().BeTrue();
            name.Success.Should().BeTrue();
            name.Value.Should().Be(expectedName);
        }

        [Fact]
        public void CanJsonSerializeCheckpointState()
        {
            var test1 = new CheckpointState(new EventSequencePoint(42));
            TestJsonRoundtrip(test1);

            var test2 = new CheckpointState(new EventSequencePoint(42), checkpointId: "TestCheckpointId");
            TestJsonRoundtrip(test2);

            var test3 = new CheckpointState(new EventSequencePoint(42), checkpointId: "TestCheckpointId", producer: new MachineLocation("This is a machine loc"));
            TestJsonRoundtrip(test3);
        }

        [Fact]
        public void CanJsonSerializeCheckpointManifest()
        {
            var test1 = new CheckpointManifest();
            test1.Add(new CheckpointManifest.ContentEntry()
            {
                Hash = ContentHash.Random(),
                Size = 2032,
                StorageId = "stoId",
                RelativePath = "/path/to/file"
            });

            TestJsonRoundtrip(test1, (t0, t1, legacySerialized) =>
            {
                Assert.Equal(t0.ContentByPath.Count, t1.ContentByPath.Count);
                if (!legacySerialized)
                {
                    Assert.Equal(t0.ContentByPath[0], t1.ContentByPath[0]);
                }
            });
        }

        [Fact]
        public void CanBackwardCompatJsonSerializeCheckpointState()
        {
            var test = new CheckpointState(new EventSequencePoint(42), checkpointId: "TestCheckpointId", producer: new MachineLocation("This is a machine loc"));
            var serialized = JsonSerializer.Serialize(test);
            var deserialized = JsonUtilities.JsonDeserialize<CheckpointState>(serialized);

            Assert.Equal(test.CheckpointId, deserialized.CheckpointId);
            Assert.Equal(test.Producer, deserialized.Producer);
        }

        private void TestJsonRoundtrip<T>(T expected, Action<T, T, bool> assertEqual = null)
        {
            assertEqual ??= (t0, t1, legacySerialized) => Assert.Equal(t0, t1);
            var serialized = JsonSerializer.Serialize(expected);
            var deserialized = JsonSerializer.Deserialize<T>(serialized);
            assertEqual(expected, deserialized, true);

            serialized = JsonSerializer.Serialize(expected);
            deserialized = JsonUtilities.JsonDeserialize<T>(serialized);
            assertEqual(expected, deserialized, true);

            serialized = JsonUtilities.JsonSerialize(expected);
            deserialized = JsonUtilities.JsonDeserialize<T>(serialized);
            assertEqual(expected, deserialized, false);
        }
    }
}

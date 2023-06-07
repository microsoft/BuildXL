// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using FluentAssertions;
using System.Text.Json;
using Xunit;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using System.Drawing;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class CheckpointManagerTests
    {
        [Fact]
        public void RocksDbCorruptionRegexMatchesSlot1()
        {
            var error =
                @"RocksDbSharp.RocksDbException: Corruption: block checksum mismatch: expected 1142279158, got 647716707  in K:\dbs\Cache\ContentAddressableStore\LocationDb\Slot1/1089482.sst offset 69135151 size 19421748
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
            var error =
                @"RocksDbSharp.RocksDbException: Corruption: block checksum mismatch: expected 1142279158, got 647716707  in K:\dbs\Cache\ContentAddressableStore\LocationDb\Slot2/1482.sst offset 69135151 size 19421748
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
            var error =
                @"RocksDbSharp.RocksDbException: Corruption: block checksum mismatch: expected 1142279158, got 647716707  in K:\dbs\Cache\ContentAddressableStore\LocationDb\Slot2\1482.sst offset 69135151 size 19421748
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
            var error =
                @"RocksDbSharp.RocksDbException: Corruption: Bad table magic number: expected 9863518390377041911, found 0 in K:\dbs\Cache\ContentAddressableStore\LocationDb\Slot2/1709103.sst
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
        public void CanJsonSerializeSequencePoints()
        {
            var test1 = new EventSequencePoint(42);
            TestJsonRoundtrip(test1);

            var test2 = new EventSequencePoint(eventStartCursorTimeUtc: DateTime.Now);
            TestJsonRoundtrip(test2);

            var test3 = EventSequencePoint.Invalid;
            TestJsonRoundtrip(test3);

            var test4 = new EventSequencePoint();
            TestJsonRoundtrip(test4);

            var test5 = new EventSequencePoint(42);
            var test5Serialized = @"{
    ""SequenceNumber"": 42,
    ""EventStartCursorTimeUtc"": null
  }";
            var test5ds = JsonSerializer.Deserialize<EventSequencePoint>(test5Serialized);
            Assert.Equal(test5, test5ds);
            var test5ds2 = JsonUtilities.JsonDeserialize<EventSequencePoint>(test5Serialized);
            Assert.Equal(test5, test5ds2);
        }

        [Fact]
        public void CanJsonSerializeCheckpointState()
        {
            var test1 = new CheckpointState(new EventSequencePoint(42));
            TestJsonRoundtrip(test1);

            var test2 = new CheckpointState(new EventSequencePoint(42), CheckpointId: "TestCheckpointId");
            TestJsonRoundtrip(test2);

            var test3 = new CheckpointState(
                new EventSequencePoint(42),
                CheckpointId: "TestCheckpointId",
                Producer: new MachineLocation("This is a machine loc"));
            TestJsonRoundtrip(test3);
        }

        [Fact]
        public void CanJsonSerializeCheckpointManifest()
        {
            var test1 = new CheckpointManifest();
            test1.Add(new CheckpointManifestContentEntry(Hash: ContentHash.Random(), Size: 2032, StorageId: "stoId", RelativePath: "/path/to/file"));

            TestJsonRoundtrip(
                test1,
                (t0, t1, legacySerialized) =>
                {
                    Assert.Equal(t0.ContentByPath.Count, t1.ContentByPath.Count);
                    if (!legacySerialized)
                    {
                        Assert.Equal(t0.ContentByPath[0], t1.ContentByPath[0]);
                    }
                });
        }

        [Fact]
        public void CanJsonDeserializeCheckpointManifest()
        {
            var serialized = @"{
  ""ContentByPath"": [
    {
      ""Hash"": ""MD5:9777E590B9643C080FD001"",
      ""RelativePath"": ""042962.sst"",
      ""StorageId"": ""MD5:9777E590B9643C080FD00108D7DFDE93||DCS||incrementalCheckpoints/333718911.f65bf138-d818-499e-ae6f-054889f16e09/042962.sst.MD5.9777E590B9643C080FD00108D7DFDE93"",
      ""Size"": 18324052
    },
    {
      ""Hash"": ""MD5:C97CF3D1E8BABE3217698C"",
      ""RelativePath"": ""044577.sst"",
      ""StorageId"": ""MD5:C97CF3D1E8BABE3217698CFC804A4F9A||DCS||incrementalCheckpoints/333913557.5200551d-09eb-4a4b-affb-2661b466516a/044577.sst.MD5.C97CF3D1E8BABE3217698CFC804A4F9A"",
      ""Size"": 19215778
    }
  ]
}";
            var test1 = new CheckpointManifest();
            test1.Add(
                new CheckpointManifestContentEntry(
                    Hash: new ShortHash("MD5:9777E590B9643C080FD001"),
                    Size: 18324052,
                    StorageId:
                    "MD5:9777E590B9643C080FD00108D7DFDE93||DCS||incrementalCheckpoints/333718911.f65bf138-d818-499e-ae6f-054889f16e09/042962.sst.MD5.9777E590B9643C080FD00108D7DFDE93",
                    RelativePath: "042962.sst"));
            test1.Add(
                new CheckpointManifestContentEntry(
                    Hash: new ShortHash("MD5:C97CF3D1E8BABE3217698C"),
                    Size: 19215778,
                    StorageId:
                    "MD5:C97CF3D1E8BABE3217698CFC804A4F9A||DCS||incrementalCheckpoints/333913557.5200551d-09eb-4a4b-affb-2661b466516a/044577.sst.MD5.C97CF3D1E8BABE3217698CFC804A4F9A",
                    RelativePath: "044577.sst"));

            TestJsonRoundtrip(
                test1,
                (t0, t1, legacySerialized) =>
                {
                    Assert.Equal(t0.ContentByPath.Count, t1.ContentByPath.Count);
                    if (!legacySerialized)
                    {
                        Assert.Equal(t0.ContentByPath[0], t1.ContentByPath[0]);
                    }
                });

            var deserialized = JsonUtilities.JsonDeserialize<CheckpointManifest>(serialized);
            Assert.True(test1.ContentByPath.SequenceEqual(deserialized.ContentByPath));
        }

        [Fact]
        public void CanBackwardCompatJsonSerializeCheckpointState()
        {
            var test = new CheckpointState(
                new EventSequencePoint(42),
                CheckpointId: "TestCheckpointId",
                Producer: new MachineLocation("This is a machine loc"));
            var serialized = JsonSerializer.Serialize(test);
            var deserialized = JsonUtilities.JsonDeserialize<CheckpointState>(serialized);

            Assert.Equal(test.CheckpointId, deserialized.CheckpointId);
            Assert.Equal(test.Producer, deserialized.Producer);
        }

        [Fact]
        public void CanBackwardsCompatDeserialize()
        {
            var test = new CheckpointState(
                new EventSequencePoint(334869783),
                CheckpointId:
                @"MD5:80B764AD2DC6B24F7EFFE7EBA3D6E412||DCS||incrementalCheckpoints/334869783.4fbf7c06-b361-4a89-9d10-a5d294bc48bf/checkpointInfo.txt.MD5.80B764AD2DC6B24F7EFFE7EBA3D6E412|Incremental",
                Producer: new MachineLocation(@"DM3APS197CDADE"),
                CheckpointTime: new DateTime(2023, 03, 08, 00, 25, 55, DateTimeKind.Utc));
            var serialized = @"{
  ""StartSequencePoint"": {
    ""SequenceNumber"": 334869783,
    ""EventStartCursorTimeUtc"": null
  },
  ""CheckpointId"": ""MD5:80B764AD2DC6B24F7EFFE7EBA3D6E412||DCS||incrementalCheckpoints/334869783.4fbf7c06-b361-4a89-9d10-a5d294bc48bf/checkpointInfo.txt.MD5.80B764AD2DC6B24F7EFFE7EBA3D6E412|Incremental"",
  ""CheckpointTime"": ""2023-03-08T00:25:55.0000000Z"",
  ""Producer"": ""DM3APS197CDADE"",
  ""Consumers"": [],
  ""CreationTimeUtc"": ""2023-03-08T00:25:55.0000000Z""
}";
            var deserialized = JsonUtilities.JsonDeserialize<CheckpointState>(serialized);

            Assert.Equal(test.CheckpointId, deserialized.CheckpointId);
            Assert.Equal(test.Producer, deserialized.Producer);
            Assert.Equal(test.CheckpointTime, deserialized.CheckpointTime);
            Assert.Equal(test.StartSequencePoint, deserialized.StartSequencePoint);
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

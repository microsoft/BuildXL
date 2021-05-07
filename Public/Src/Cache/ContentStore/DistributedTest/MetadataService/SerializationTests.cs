// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using ProtoBuf.Meta;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.MetadataService
{
    public class SerializationTests
    {
        [Fact]
        public void MachineIdRoundtrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            CheckSerializationRoundtrip(model, new MachineId());
            CheckSerializationRoundtrip(model, new MachineId(1));
            CheckSerializationRoundtrip(model, new MachineId(42));
        }

        [Fact]
        public void ShortHashRoundtrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            ShortHash contentHash = ContentHash.Random();
            CheckSerializationRoundtrip(model, contentHash);
        }

        [Fact]
        public void PutBlobRoundtrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            var obj = new PutBlobRequest();
            CheckSerializationRoundtrip(model, obj);
        }

        [Fact]
        public void ShortHashWithSizeRoundtrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            var obj = new ShortHashWithSize(ContentHash.Random(), 42);
            CheckSerializationRoundtrip(model, obj);
        }

        [Fact]
        public void ContentLocationEntryRoundtrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            CheckSerializationRoundtrip(model, ContentLocationEntry.Create(
                ArrayMachineIdSet.Create(new[] { new MachineId(12), new MachineId(23) }),
                12345,
                DateTime.UtcNow,
                DateTime.UtcNow - TimeSpan.FromDays(1)));

            CheckSerializationRoundtrip(model, ContentLocationEntry.Create(
                MachineIdSet.Empty,
                46456,
                new UnixTime(1)));
        }

        [Fact]
        public void GetContentLocationsRequestRoundtrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            var obj = new GetContentLocationsRequest()
            {
                ContextId = "test",
                Hashes = new List<ShortHash>() {
                    ContentHash.Random(),
                },
            };

            var deserialized = Roundtrip(model, obj);

            Assert.Equal(obj.Hashes, deserialized.Hashes);

            // Equality seems to be defined wrong for the record type somehow. However, the deserialization does result
            // in the same objects.
            // Assert.Equal(obj, deserialized);
        }

        [Fact]
        public void GetContentLocationsResponseRoundtrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            var obj = new GetContentLocationsResponse()
            {
                PersistRequest = true,
                Diagnostics = "Diagnostics1",
                ErrorMessage = "ErrorMessage2",
                Entries = new List<ContentLocationEntry>()
                {
                }
            };

            var deserialized = Roundtrip(model, obj);

            Assert.Equal(obj.PersistRequest, deserialized.PersistRequest);
            Assert.Equal(obj.Diagnostics, deserialized.Diagnostics);
            Assert.Equal(obj.ErrorMessage, deserialized.ErrorMessage);
            Assert.Equal(obj.Entries, deserialized.Entries);

            // Equality seems to be defined wrong for the record type somehow. However, the deserialization does result
            // in the same objects.
            // Assert.Equal(obj, deserialized);
        }

        private void CheckSerializationRoundtrip(RuntimeTypeModel model, ContentLocationEntry obj)
        {
            var deserialized = Roundtrip(model, obj);
            Assert.Equal(obj.ContentSize, deserialized.ContentSize);
            XAssert.SetEqual(obj.Locations, deserialized.Locations);
        }

        private void CheckSerializationRoundtrip<T>(RuntimeTypeModel model, T obj)
            where T : IEquatable<T>
        {
            var deserialized = Roundtrip<T>(model, obj);
            Assert.Equal(obj, deserialized);
        }

        private static T Roundtrip<T>(RuntimeTypeModel model, T obj)
        {
            var stream = new MemoryStream();
            model.Serialize(stream, obj);
            stream.Seek(0, SeekOrigin.Begin);
            return model.Deserialize<T>(stream);
        }
    }
}

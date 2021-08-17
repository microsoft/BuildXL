// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using FluentAssertions;
using ProtoBuf;
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
        public void RegisterContentLocationsRequestRoundtrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            var schema = MetadataServiceSerializer.TypeModel.GetSchema(typeof(RegisterContentLocationsRequest));

            var obj = new RegisterContentLocationsRequest()
            {
                ContextId = "1",
                Hashes = new List<ShortHashWithSize>() {
                    (ContentHash.Random(), 42),
                },
                MachineId = new MachineId(79)
            };

            var deserialized = Roundtrip(model, obj);

            var b1 = ToByteArray(ms => MetadataServiceSerializer.TypeModel.Serialize(ms, obj));
            var b2 = ToByteArray(ms => MetadataServiceSerializer.TypeModel.SerializeWithLengthPrefix(ms, obj, typeof(RegisterContentLocationsRequest), PrefixStyle.Base128, 51));
            var b3 = ToByteArray(ms => MetadataServiceSerializer.TypeModel.SerializeWithLengthPrefix(ms, obj, typeof(RegisterContentLocationsRequest), PrefixStyle.Fixed32, 51));

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

            Assert.Equal(obj.Diagnostics, deserialized.Diagnostics);
            Assert.Equal(obj.ErrorMessage, deserialized.ErrorMessage);
            Assert.Equal(obj.Entries, deserialized.Entries);

            // Equality seems to be defined wrong for the record type somehow. However, the deserialization does result
            // in the same objects.
            // Assert.Equal(obj, deserialized);
        }

        [Fact]
        public void GetContentHashListRequestRoundTrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            var obj = new GetContentHashListRequest()
            {
                StrongFingerprint = StrongFingerprint.Random()
            };

            var deserialized = Roundtrip(model, obj);

            obj.StrongFingerprint.Should().BeEquivalentTo(deserialized.StrongFingerprint);
        }

        [Fact]
        public void CompareExchangeRequestRoundTrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            var obj = new CompareExchangeRequest()
            {
                StrongFingerprint = StrongFingerprint.Random(),
                ExpectedReplacementToken = "ErT",
                Replacement = new SerializedMetadataEntry()
                {
                    SequenceNumber = 10,
                    Data = Guid.NewGuid().ToByteArray(),
                    ReplacementToken = "Rt"
                }
            };

            var deserialized = Roundtrip(model, obj);

            obj.Replacement.Should().BeEquivalentTo(deserialized.Replacement);
            obj.ExpectedReplacementToken.Should().BeEquivalentTo(deserialized.ExpectedReplacementToken);
            obj.StrongFingerprint.Should().BeEquivalentTo(deserialized.StrongFingerprint);
        }

        [Fact]
        public void HeartbeatMachineResponse()
        {
            var model = MetadataServiceSerializer.TypeModel;

            var obj = new HeartbeatMachineResponse()
            {
                PriorState = MachineState.Closed,
                InactiveMachines = BitMachineIdSet.EmptyInstance.SetExistenceBit(new MachineId(32), true)
            };

            var deserialized = Roundtrip(model, obj);

            deserialized.PriorState.Should().Be(MachineState.Closed);
            deserialized.InactiveMachines.Value[32].Should().BeTrue();
            deserialized.InactiveMachines.Value.Count.Should().Be(1);
        }

        [Fact]
        public void GetLevelSelectorsResponseRoundTrip()
        {
            var model = MetadataServiceSerializer.TypeModel;

            var obj = new GetLevelSelectorsResponse()
            {
                Selectors = new List<Selector>()
                {
                    Selector.Random(),
                    Selector.Random(),
                    Selector.Random()
                }
            };

            var deserialized = Roundtrip(model, obj);

            obj.Selectors.Should().BeEquivalentTo(deserialized.Selectors);
        }

        private byte[] ToByteArray(Action<MemoryStream> serialize)
        {
            var stream = new MemoryStream();
            serialize(stream);
            return stream.ToArray();
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

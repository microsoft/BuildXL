// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Newtonsoft.Json;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    public class SerializationTests
    {
        private const int RandomBytesSize = 29;

        [Fact]
        public void TestSerializeDeserializeStrongFingerprint()
        {
            var sfp = StrongFingerprint.Random();

            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.Converters.Add(new StrongFingerprintConverter());
            string serializedFingerprint = JsonConvert.SerializeObject(sfp, serializerSettings);

            StrongFingerprint deserializedFingerprint =
                JsonConvert.DeserializeObject<StrongFingerprint>(serializedFingerprint, serializerSettings);

            sfp.Should().Be(deserializedFingerprint);
        }

        [Fact]
        public void TestSerializeDeserializeSelector()
        {
            var sfp = StrongFingerprint.Random();

            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.Converters.Add(new SelectorConverter());
            string serializedSelector = JsonConvert.SerializeObject(sfp.Selector, serializerSettings);

            Selector deserializedSelector =
                JsonConvert.DeserializeObject<Selector>(serializedSelector, serializerSettings);

            sfp.Selector.Should().Be(deserializedSelector);
        }

        [Fact]
        public void TestSerializeDeserializeSelectorNoOutput()
        {
            var selector = new Selector(ContentHash.Random());
            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.Converters.Add(new SelectorConverter());
            string serializedSelector = JsonConvert.SerializeObject(selector, serializerSettings);

            Selector deserializedSelector =
                JsonConvert.DeserializeObject<Selector>(serializedSelector, serializerSettings);

            selector.Should().Be(deserializedSelector);
        }

        [Fact]
        public void TestSerializeDeserializeContentHashLists()
        {
            var contentHashes = new[]
            {
                ContentHash.Random(),
                ContentHash.Random(),
                ContentHash.Random()
            };
            byte[] payload = ThreadSafeRandom.GetBytes(RandomBytesSize);

            var chl = new ContentHashList(contentHashes.ToArray(), payload);

            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.Converters.Add(new ContentHashListWithDeterminismConverter());
            string serializedSelector = JsonConvert.SerializeObject(new ContentHashListWithDeterminism(chl, CacheDeterminism.None), serializerSettings);

            ContentHashListWithDeterminism deserializeObject =
                JsonConvert.DeserializeObject<ContentHashListWithDeterminism>(serializedSelector, serializerSettings);

            chl.Should().Be(deserializeObject.ContentHashList);
        }

        [Fact]
        public void TestSerializeDeserializeContentHashListsNoPayload()
        {
            var contentHashes = new[]
            {
                ContentHash.Random(),
                ContentHash.Random(),
                ContentHash.Random()
            };

            var chl = new ContentHashList(contentHashes.ToArray());

            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.Converters.Add(new ContentHashListWithDeterminismConverter());
            string serializedSelector = JsonConvert.SerializeObject(new ContentHashListWithDeterminism(chl, CacheDeterminism.None), serializerSettings);

            ContentHashListWithDeterminism deserializeObject =
                JsonConvert.DeserializeObject<ContentHashListWithDeterminism>(serializedSelector, serializerSettings);

            chl.Should().Be(deserializeObject.ContentHashList);
        }

        [Fact]
        public void TestSerializeDeserializeContentHashListsNoHashes()
        {
            var chl = new ContentHashList(new ContentHash[0]);

            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.Converters.Add(new ContentHashListWithDeterminismConverter());
            string serializedSelector = JsonConvert.SerializeObject(new ContentHashListWithDeterminism(chl, CacheDeterminism.None), serializerSettings);

            var deserializeObject =
                JsonConvert.DeserializeObject<ContentHashListWithDeterminism>(serializedSelector, serializerSettings);

            chl.Should().Be(deserializeObject.ContentHashList);
        }

        [Fact]
        public void TestSerializeDeserializeContentHashListsNoHashesWithPayload()
        {
            byte[] payload = ThreadSafeRandom.GetBytes(RandomBytesSize);
            var chl = new ContentHashList(new ContentHash[0], payload);

            var serializerSettings = new JsonSerializerSettings();
            serializerSettings.Converters.Add(new ContentHashListWithDeterminismConverter());
            string serializedContentHashList = JsonConvert.SerializeObject(new ContentHashListWithDeterminism(chl, CacheDeterminism.None), serializerSettings);

            var deserializeObject =
                JsonConvert.DeserializeObject<ContentHashListWithDeterminism>(serializedContentHashList, serializerSettings);

            chl.Should().Be(deserializeObject.ContentHashList);
        }
    }
}

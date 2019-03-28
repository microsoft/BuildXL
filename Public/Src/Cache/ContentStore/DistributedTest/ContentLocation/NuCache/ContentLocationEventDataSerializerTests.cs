// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;
using BuildXL.Cache.ContentStore.Hashing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class ContentLocationEventDataSerializerTests : TestBase
    {
        /// <nodoc />
        public ContentLocationEventDataSerializerTests(ITestOutputHelper output)
        : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public void LargeInstanceIsSplitAutomatically()
        {
            const int numberOfHashesPerItem = 20000;
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            var largeMessage = GenerateRandomEventData(0, numberOfHashesPerItem, touchTime);
            var serializedMessages = serializer.Serialize(OperationContext(), new[] { largeMessage }).ToList();

            // Round trip validation is performed by the serializer
            Output.WriteLine($"Number of serialized records: {serializedMessages.Count}");
            serializedMessages.Count.Should().NotBe(1);
        }

        [Fact]
        public void TwoHunderdEventsShouldBeSerializedIntoOneEventDAta()
        {
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            var largeMessage = Enumerable.Range(1, 200).Select(n => GenerateRandomEventData(0, numberOfHashes: 2, touchTime: touchTime)).ToArray();
            var serializedMessages = serializer.Serialize(OperationContext(), largeMessage).ToList();

            // Round trip validation is performed by the serializer
            Output.WriteLine($"Number of serialized records: {serializedMessages.Count}");
            serializedMessages.Count.Should().Be(1);
        }

        [Fact]
        public void CreateEventDatasReturnsMoreThanOneValueDueToTheSize()
        {
            const int numberOfItems = 1000;
            const int numberOfHashesPerItem = 200;
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            var messages = Enumerable.Range(1, numberOfItems).Select(n => GenerateRandomEventData(n, numberOfHashesPerItem, touchTime)).ToArray();

            // Round trip validation is performed by the serializer
            var serializedMessages = serializer.Serialize(OperationContext(), messages).ToList();

            Output.WriteLine($"Number of serialized records: {serializedMessages.Count}");
            serializedMessages.Count.Should().NotBe(1);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(10, 2)]
        public void SerializationRoundtripWithSmallNumberOfElements(int numberOfItems, int numberOfHashesPerItem)
        {
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            var messages = Enumerable.Range(1, numberOfItems).Select(n => GenerateRandomEventData(n, numberOfHashesPerItem, touchTime)).ToArray();

            // Round trip validation is performed by the serializer
            var serializedMessages = serializer.Serialize(OperationContext(), messages).ToList();
            serializedMessages.Count.Should().Be(1); // All the cases we have should fit into one message.
        }

        private static ContentLocationEventData GenerateRandomEventData(int index, int numberOfHashes, DateTime touchTime)
        {
            var random = new Random(index);
            var hashesAndSizes = Enumerable.Range(1, numberOfHashes).Select(n => (hash: new ShortHash(ContentHash.Random()), size: (long)random.Next(10_000_000))).ToList();
            switch (index % 3)
            {
                case 0:
                    return new AddContentLocationEventData(new MachineId(index), hashesAndSizes.SelectArray(n => n.hash), hashesAndSizes.SelectArray(n => n.size));
                case 1:
                    return new TouchContentLocationEventData(new MachineId(index), hashesAndSizes.SelectArray(n => n.hash), touchTime);
                default:
                    return new RemoveContentLocationEventData(new MachineId(index), hashesAndSizes.SelectArray(n => n.hash));
            }
        }

        private static ContentLocationEventDataSerializer CreateContentLocationEventDataSerializer() => new ContentLocationEventDataSerializer(ValidationMode.Fail);

        private static OperationContext OperationContext() => new OperationContext(new Context(TestGlobal.Logger));
    }
}

// --------------------------------------------------------------------
//  
// Copyright (c) Microsoft Corporation.  All rights reserved.
//  
// --------------------------------------------------------------------

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Service;
using FluentAssertions;
using Xunit;
using StrongFingerprint = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.StrongFingerprint;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    public class GrpcDataConverterTests
    {
        [Fact]
        public void SerializeSelector()
        {
            var selector = RandomSelector();
            var deserializedSelector = selector.ToGrpc().FromGrpc();

            Assert.Equal(selector, deserializedSelector);
        }

        [Fact]
        public void SerializeSelectorWithNullOutputReturnsEmptyOutput()
        {
            var selector = new Selector(ContentHash.Random());
            var deserializedSelector = selector.ToGrpc().FromGrpc();

            deserializedSelector.Output.Should().NotBeNull();
        }

        [Fact]
        public void SerializeContentHashList()
        {
            var contentHashList = RandomContentHashList();
            var deserializedContentHashList = contentHashList.ToGrpc().FromGrpc();
            Assert.Equal(contentHashList, deserializedContentHashList);
        }

        [Fact]
        public void SerializeContentHashListWithDeterminism()
        {
            var input = new ContentHashListWithDeterminism(RandomContentHashList(), RandomCacheDeterminism());
            var deserialized = input.ToGrpc().FromGrpc();
            Assert.Equal(input, deserialized);
        }

        [Fact]
        public void SerializeEmptyContentHashList()
        {
            var input = new ContentHashListWithDeterminism();
            var deserialized = input.ToGrpc().FromGrpc();
            Assert.Equal(input, deserialized);
        }

        [Fact]
        public void SerializeStrongFingerprint()
        {
            var strongFingerprint = RandomStrongFingerprint();
            var deserialized = strongFingerprint.ToGrpc().FromGrpc();

            Assert.Equal(strongFingerprint, deserialized);
        }

        private static StrongFingerprint RandomStrongFingerprint()
        {
            return new StrongFingerprint(Fingerprint.Random(), RandomSelector());
        }

        private static CacheDeterminism RandomCacheDeterminism()
        {
            return CacheDeterminism.ViaCache(Guid.NewGuid(), DateTime.UtcNow);
        }

        private static ContentHashList RandomContentHashList()
        {
            return new ContentHashList(Enumerable.Range(1, 10).Select(n => ContentHash.Random()).ToArray(), ContentHash.Random().ToByteArray());
        }

        private static Selector RandomSelector()
        {
            return new Selector(ContentHash.Random(), ContentHash.Random().ToByteArray());
        }
    }
}

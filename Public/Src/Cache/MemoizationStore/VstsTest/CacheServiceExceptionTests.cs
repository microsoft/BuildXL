// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using FluentAssertions;
using BuildXL.Cache.MemoizationStore.VstsInterfaces;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.VstsTest
{
    [Trait("Category", "QTestSkip")]
    public class CacheServiceExceptionTests
    {
        [Fact]
        public void DefaultSerializesToStream()
        {
            SerializesToStream(new CacheServiceException());
        }

        [Theory]
        [InlineData(CacheErrorReasonCode.Unknown)]
        [InlineData(CacheErrorReasonCode.BlobFinalizationFailure)]
        [InlineData(CacheErrorReasonCode.ContentHashListNotFound)]
        [InlineData(CacheErrorReasonCode.IncorporateFailed)]
        public void ReasonCodeSerializesToStream(CacheErrorReasonCode reasonCode)
        {
            SerializesToStream(new CacheServiceException("Error message", reasonCode));
        }

        private void SerializesToStream(CacheServiceException ex)
        {
            using (var stream = new MemoryStream())
            {
                BinaryFormatter serializer = new BinaryFormatter();
                serializer.Serialize(stream, ex);
                stream.Length.Should().BeGreaterThan(0);

                using (var stream2 = new MemoryStream(stream.ToArray()))
                {
                    var newEx = (CacheServiceException)serializer.Deserialize(stream2);
                    newEx.ReasonCode.Should().Be(ex.ReasonCode);
                    newEx.Message.Should().Be(ex.Message);
                    newEx.StackTrace.Should().Be(ex.StackTrace);
                    newEx.ToString().Should().Be(ex.ToString());
                }
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
#pragma warning disable SYSLIB0011 // Type or member is obsolete
                serializer.Serialize(stream, ex);
#pragma warning restore SYSLIB0011 // Type or member is obsolete
                stream.Length.Should().BeGreaterThan(0);

                using (var stream2 = new MemoryStream(stream.ToArray()))
                {
#pragma warning disable CA2300, CA2301 // Disable CA2301 Do not call BinaryFormatter.Deserialize without first setting BinaryFormatter.Binder
#pragma warning disable SYSLIB0011 // Type or member is obsolete
                    var newEx = (CacheServiceException)serializer.Deserialize(stream2);
#pragma warning restore SYSLIB0011 // Type or member is obsolete
#pragma warning restore CA2300, CA2301 // Restore CA2301 Do not call BinaryFormatter.Deserialize without first setting BinaryFormatter.Binder
                    newEx.ReasonCode.Should().Be(ex.ReasonCode);
                    newEx.Message.Should().Be(ex.Message);
                    newEx.StackTrace.Should().Be(ex.StackTrace);
                    newEx.ToString().Should().Be(ex.ToString());
                }
            }
        }
    }
}

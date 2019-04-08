// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using BuildXL.Cache.ContentStore.Exceptions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Exceptions
{
    public abstract class CacheExceptionBaseTests : TestBase
    {
        private const string Message = "message";
        private const string Format = "format {0}";
        private const string Arg = "arg";
        private const string Formatted = "format arg";
        private readonly Exception _innerException = new IOException();

        protected abstract CacheException Construct();

        protected CacheExceptionBaseTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void DefaultConstructorPresent()
        {
            Construct().Should().NotBeNull();
        }

        [Fact]
        public void MessageConstructor()
        {
            var exception = new CacheException(Message);
            exception.Message.Should().Be(Message);
        }

        [Fact]
        public void FormatConstructor()
        {
            var exception = new CacheException(Format, Arg);
            exception.Message.Should().Be(Formatted);
        }

        [Fact]
        public void MessageExceptionConstructor()
        {
            var exception = new CacheException(Message, _innerException);
            exception.Message.Should().Be(Message);
            exception.InnerException.Should().Be(_innerException);
        }

        [Fact]
        public void ExceptionFormatConstructor()
        {
            var exception = new CacheException(_innerException, Format, Arg);
            exception.InnerException.Should().Be(_innerException);
            exception.Message.Should().Be(Formatted);
        }

        [Fact]
        public void SerializesToStream()
        {
            var ex = Construct();

            using (var stream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, ex);
                stream.Length.Should().BeGreaterThan(0);

                using (var stream2 = new MemoryStream(stream.ToArray()))
                {
                    var dex = (CacheException)formatter.Deserialize(stream2);
                    dex.Should().NotBeNull();
                }
            }
        }
    }
}

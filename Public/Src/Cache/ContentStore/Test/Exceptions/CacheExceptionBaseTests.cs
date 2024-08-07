// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
#pragma warning disable SYSLIB0011 // IFormatter.Serialize is obsolete              
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, ex);
                stream.Length.Should().BeGreaterThan(0);

                using (var stream2 = new MemoryStream(stream.ToArray()))
                {
                    // CodeQL [SM04191] It is fine not to use Binder here. This is a test, we serialize our own class, and we control its content.
                    var dex = (CacheException)formatter.Deserialize(stream2);
                    dex.Should().NotBeNull();
                }
#pragma warning restore SYSLIB0011
            }
        }
    }
}

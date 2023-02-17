// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using StackExchange.Redis;
using Xunit;

namespace Test.BuildXL.Utilities
{
    public class ExceptionUtilitiesTests
    {
        [Fact]
        public void ClassifyMissingRuntimeDependencyTest()
        {
            Assert.Equal(ExceptionRootCause.MissingRuntimeDependency, ExceptionUtilities.AnalyzeExceptionRootCause(new FileLoadException()));
            Assert.Equal(ExceptionRootCause.MissingRuntimeDependency, ExceptionUtilities.AnalyzeExceptionRootCause(new FileNotFoundException("Could not load file or assembly")));
            Assert.Equal(ExceptionRootCause.MissingRuntimeDependency, ExceptionUtilities.AnalyzeExceptionRootCause(new DllNotFoundException()));
            Assert.Equal(ExceptionRootCause.MissingRuntimeDependency, ExceptionUtilities.AnalyzeExceptionRootCause(new TypeLoadException()));
        }

        [Fact]
        public void ClassifyOutOfDiskSpace()
        {
            Assert.Equal(ExceptionRootCause.OutOfDiskSpace, ExceptionUtilities.AnalyzeExceptionRootCause(new IOException("No space left on device")));
        }

        [Fact]
        public void RedisConnectionExceptionIsKnownUnobservedException()
        {
            var redisException = new RedisConnectionException("Message");
            Assert.True(
                ExceptionUtilities.IsKnownUnobservedException(redisException));

            Assert.True(ExceptionUtilities.IsKnownUnobservedException(new AggregateException(new Exception("1"), redisException)));
        }
        
        [Fact]
        public void UnknownRedisConnectionExceptionIsNotKnownUnobservedException()
        {
            var unknown = new UnknownRedisConnectionException("Message");
            Assert.False(
                ExceptionUtilities.IsKnownUnobservedException(unknown));

            Assert.False(ExceptionUtilities.IsKnownUnobservedException(new AggregateException(new Exception("1"), unknown)));
        }
    }
}

namespace StackExchange.Redis
{
    public class RedisConnectionException : Exception
    {
        public RedisConnectionException(string message)
            : base(message)
        {

        }
    }

    public class UnknownRedisConnectionException : Exception
    {
        public UnknownRedisConnectionException(string message)
            : base(message)
        {

        }
    }
}
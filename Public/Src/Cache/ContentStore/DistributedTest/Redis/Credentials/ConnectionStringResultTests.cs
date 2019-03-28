// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using Xunit;

namespace ContentStoreTest.Distributed.Redis.Credentials
{
    public class ConnectionStringResultTests : ResultTests<ConnectionStringResult>
    {
        private const string ConnectionString = "ConnectionString";

        protected override ConnectionStringResult CreateFrom(Exception exception)
        {
            return ConnectionStringResult.CreateFailure(exception);
        }

        protected override ConnectionStringResult CreateFrom(string errorMessage)
        {
            return ConnectionStringResult.CreateFailure(errorMessage);
        }

        protected override ConnectionStringResult CreateFrom(string errorMessage, string diagnostics)
        {
            return ConnectionStringResult.CreateFailure(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.False(ConnectionStringResult.CreateFailure(other, "message").Succeeded);
        }

        [Fact]
        public void ExceptionError()
        {
            Assert.False(CreateFrom(new InvalidOperationException()).Succeeded);
        }

        [Fact]
        public void ErrorMessageError()
        {
            Assert.False(CreateFrom("error").Succeeded);
        }

        [Fact]
        public void Success()
        {
            Assert.True(ConnectionStringResult.CreateSuccess(ConnectionString).Succeeded);
        }

        [Fact]
        public void ConnectionStringProperty()
        {
            Assert.Equal(ConnectionString, ConnectionStringResult.CreateSuccess(ConnectionString).ConnectionString);
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            var v1 = ConnectionStringResult.CreateSuccess(ConnectionString);
            var v2 = ConnectionStringResult.CreateSuccess(ConnectionString) as object;
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            var v1 = ConnectionStringResult.CreateSuccess(ConnectionString) as object;
            var v2 = new object();
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrue()
        {
            var v1 = ConnectionStringResult.CreateSuccess(ConnectionString);
            var v2 = ConnectionStringResult.CreateSuccess(ConnectionString);
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            var v1 = ConnectionStringResult.CreateSuccess(ConnectionString);
            var v2 = ConnectionStringResult.CreateFailure("error");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void EqualsFalseErrorMessageMismatch()
        {
            var v1 = ConnectionStringResult.CreateFailure("error1");
            var v2 = ConnectionStringResult.CreateFailure("error2");
            Assert.False(v1.Equals(v2));
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            var v1 = ConnectionStringResult.CreateSuccess(ConnectionString);
            var v2 = ConnectionStringResult.CreateSuccess(ConnectionString);
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            var v1 = ConnectionStringResult.CreateSuccess(ConnectionString);
            var v2 = ConnectionStringResult.CreateFailure("error");
            Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", ConnectionStringResult.CreateFailure("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains("Success", ConnectionStringResult.CreateSuccess(ConnectionString).ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

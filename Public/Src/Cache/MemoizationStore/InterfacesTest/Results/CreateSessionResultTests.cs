// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Results
{
    public class CreateSessionResultTests : ResultTests<CreateSessionResult<IReadOnlyCacheSession>>
    {
        protected override CreateSessionResult<IReadOnlyCacheSession> CreateFrom(Exception exception)
        {
            return new CreateSessionResult<IReadOnlyCacheSession>(exception);
        }

        protected override CreateSessionResult<IReadOnlyCacheSession> CreateFrom(string errorMessage)
        {
            return new CreateSessionResult<IReadOnlyCacheSession>(errorMessage);
        }

        protected override CreateSessionResult<IReadOnlyCacheSession> CreateFrom(string errorMessage, string diagnostics)
        {
            return new CreateSessionResult<IReadOnlyCacheSession>(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.False(new CreateSessionResult<IReadOnlyCacheSession>(other, "message").Succeeded);
        }

        [Fact(Skip = "This test fails with NRE! Please, please, remove equality implementation from results! Bug #1331026")]
        public void GetHashCodeOnFailure()
        {
            Assert.NotEqual(0, CreateFrom("error").GetHashCode());
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
        public void CodePropertySuccess()
        {
            using (var session = new ThrowingCacheSession())
            {
                Assert.True(new CreateSessionResult<IReadOnlyCacheSession>(session).Succeeded);
            }
        }

        [Fact]
        public void CodePropertyError()
        {
            Assert.False(new CreateSessionResult<IReadOnlyCacheSession>("error").Succeeded);
        }

        [Fact]
        public void SessionProperty()
        {
            using (var session = new ThrowingCacheSession())
            {
                Assert.Equal(session, new CreateSessionResult<IReadOnlyCacheSession>(session).Session);
            }
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            using (var session = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<IReadOnlyCacheSession>(session);
                var v2 = new CreateSessionResult<IReadOnlyCacheSession>(session) as object;
                Assert.True(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            using (var session = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<IReadOnlyCacheSession>(session);
                var v2 = new object();
                Assert.False(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsTrue()
        {
            using (var session = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<IReadOnlyCacheSession>(session);
                var v2 = new CreateSessionResult<IReadOnlyCacheSession>(session);
                Assert.True(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsTrueNotReferenceEqualSession()
        {
            using (var session1 = new ThrowingCacheSession())
            using (var session2 = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<IReadOnlyCacheSession>(session1);
                var v2 = new CreateSessionResult<IReadOnlyCacheSession>(session2);
                Assert.True(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            using (var session1 = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<IReadOnlyCacheSession>(session1);
                var v2 = new CreateSessionResult<IReadOnlyCacheSession>("error");
                Assert.False(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsFalseSessionMismatch()
        {
            using (var session1 = new ThrowingCacheSession("session1"))
            using (var session2 = new ThrowingCacheSession("session2"))
            {
                var v1 = new CreateSessionResult<IReadOnlyCacheSession>(session1);
                var v2 = new CreateSessionResult<IReadOnlyCacheSession>(session2);
                Assert.False(v1.Equals(v2));
            }
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            using (var session1 = new ThrowingCacheSession("session1"))
            using (var session2 = new ThrowingCacheSession("session1"))
            {
                var v1 = new CreateSessionResult<IReadOnlyCacheSession>(session1);
                var v2 = new CreateSessionResult<IReadOnlyCacheSession>(session2);
                Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
            }
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            using (var session1 = new ThrowingCacheSession("session1"))
            using (var session2 = new ThrowingCacheSession("session2"))
            {
                var v1 = new CreateSessionResult<IReadOnlyCacheSession>(session1);
                var v2 = new CreateSessionResult<IReadOnlyCacheSession>(session2);
                Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
            }
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains(
                "something", new CreateSessionResult<IReadOnlyCacheSession>("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            using (var session = new ThrowingCacheSession())
            {
                var result = new CreateSessionResult<IReadOnlyCacheSession>(session);
                Assert.Contains("Success", result.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

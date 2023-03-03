// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace BuildXL.Cache.MemoizationStore.InterfacesTest.Results
{
    public class CreateSessionResultTests : ResultTests<CreateSessionResult<ICacheSession>>
    {
        protected override CreateSessionResult<ICacheSession> CreateFrom(Exception exception)
        {
            return new CreateSessionResult<ICacheSession>(exception);
        }

        protected override CreateSessionResult<ICacheSession> CreateFrom(string errorMessage)
        {
            return new CreateSessionResult<ICacheSession>(errorMessage);
        }

        protected override CreateSessionResult<ICacheSession> CreateFrom(string errorMessage, string diagnostics)
        {
            return new CreateSessionResult<ICacheSession>(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.False(new CreateSessionResult<ICacheSession>(other, "message").Succeeded);
        }

        [Fact]
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
                Assert.True(new CreateSessionResult<ICacheSession>(session).Succeeded);
            }
        }

        [Fact]
        public void CodePropertyError()
        {
            Assert.False(new CreateSessionResult<ICacheSession>("error").Succeeded);
        }

        [Fact]
        public void SessionProperty()
        {
            using (var session = new ThrowingCacheSession())
            {
                Assert.Equal(session, new CreateSessionResult<ICacheSession>(session).Session);
            }
        }

        [Fact]
        public void EqualsObjectTrue()
        {
            using (var session = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<ICacheSession>(session);
                var v2 = new CreateSessionResult<ICacheSession>(session) as object;
                Assert.True(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsObjectFalse()
        {
            using (var session = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<ICacheSession>(session);
                var v2 = new object();
                Assert.False(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsTrue()
        {
            using (var session = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<ICacheSession>(session);
                var v2 = new CreateSessionResult<ICacheSession>(session);
                Assert.True(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsTrueForInvalidSessions()
        {
            var v1 = new CreateSessionResult<ICacheSession>("error1");
            var v2 = new CreateSessionResult<ICacheSession>("error1");
            Assert.True(v1.Equals(v2));
        }

        [Fact]
        public void EqualsTrueNotReferenceEqualSession()
        {
            using (var session1 = new ThrowingCacheSession())
            using (var session2 = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<ICacheSession>(session1);
                var v2 = new CreateSessionResult<ICacheSession>(session2);
                Assert.True(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsFalseCodeMismatch()
        {
            using (var session1 = new ThrowingCacheSession())
            {
                var v1 = new CreateSessionResult<ICacheSession>(session1);
                var v2 = new CreateSessionResult<ICacheSession>("error");
                Assert.False(v1.Equals(v2));
            }
        }

        [Fact]
        public void EqualsFalseSessionMismatch()
        {
            using (var session1 = new ThrowingCacheSession("session1"))
            using (var session2 = new ThrowingCacheSession("session2"))
            {
                var v1 = new CreateSessionResult<ICacheSession>(session1);
                var v2 = new CreateSessionResult<ICacheSession>(session2);
                Assert.False(v1.Equals(v2));
            }
        }

        [Fact]
        public void GetHashCodeSameWhenEqual()
        {
            using (var session1 = new ThrowingCacheSession("session1"))
            using (var session2 = new ThrowingCacheSession("session1"))
            {
                var v1 = new CreateSessionResult<ICacheSession>(session1);
                var v2 = new CreateSessionResult<ICacheSession>(session2);
                Assert.Equal(v1.GetHashCode(), v2.GetHashCode());
            }
        }

        [Fact]
        public void GetHashCodeNotSameWhenNotEqual()
        {
            using (var session1 = new ThrowingCacheSession("session1"))
            using (var session2 = new ThrowingCacheSession("session2"))
            {
                var v1 = new CreateSessionResult<ICacheSession>(session1);
                var v2 = new CreateSessionResult<ICacheSession>(session2);
                Assert.NotEqual(v1.GetHashCode(), v2.GetHashCode());
            }
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains(
                "something", new CreateSessionResult<ICacheSession>("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            using (var session = new ThrowingCacheSession())
            {
                var result = new CreateSessionResult<ICacheSession>(session);
                Assert.Contains("Success", result.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

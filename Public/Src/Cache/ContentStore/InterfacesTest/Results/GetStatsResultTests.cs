// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class GetStatsResultTests : ResultTests<GetStatsResult>
    {
        private readonly CounterSet _counters = new CounterSet();

        protected override GetStatsResult CreateFrom(Exception exception)
        {
            return new GetStatsResult(exception);
        }

        protected override GetStatsResult CreateFrom(string errorMessage)
        {
            return new GetStatsResult(errorMessage);
        }

        protected override GetStatsResult CreateFrom(string errorMessage, string diagnostics)
        {
            return new GetStatsResult(errorMessage, diagnostics);
        }

        [Fact]
        public void ConstructFromResultBase()
        {
            var other = new BoolResult("error");
            Assert.False(new GetStatsResult(other, "message").Succeeded);
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
        public void SucceededTrue()
        {
            Assert.True(new GetStatsResult(_counters).Succeeded);
        }

        [Fact]
        public void SucceededFalse()
        {
            Assert.False(new GetStatsResult("error").Succeeded);
        }

        [Fact]
        public void ToStringWithError()
        {
            Assert.Contains("something", new GetStatsResult("something").ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ToStringSuccess()
        {
            Assert.Contains("Success", new GetStatsResult(_counters).ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

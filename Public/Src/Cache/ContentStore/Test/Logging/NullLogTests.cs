// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Logging
{
    public class NullLogTests : TestBase
    {
        private const string Message = "some message";

        public NullLogTests()
            : base(TestGlobal.Logger)
        {
        }

        [Fact]
        public void UsingPatternSupported()
        {
            using (new NullLog())
            {
            }
        }

        [Fact]
        public void InstanceAvailable()
        {
            NullLog.Instance.Write(DateTime.Now, Thread.CurrentThread.ManagedThreadId, Severity.Error, Message);
        }

        [Fact]
        public void CurrentSeverityGivesLowest()
        {
            NullLog.Instance.CurrentSeverity.Should().Be(Severity.Diagnostic);
        }
    }
}

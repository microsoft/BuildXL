// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public abstract class ResultTests<T>
        where T : ResultBase
    {
        protected abstract T CreateFrom(Exception exception);

        protected abstract T CreateFrom(string errorMessage);

        protected abstract T CreateFrom(string errorMessage, string diagnostics);

        [Fact]
        public void DiagnosticsContainsExceptionInfo()
        {
            Exception exception;
            try
            {
                throw new InvalidOperationException("test");
            }
            catch (InvalidOperationException e)
            {
                exception = e;
            }

            T result = CreateFrom(exception);

            Assert.Contains("test", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("InvalidOperationException", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("  at", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DiagnosticsContainsAggregateExceptionInfo()
        {
            Exception exception1;
            Exception exception2;

            try
            {
                throw new InvalidOperationException("test1");
            }
            catch (InvalidOperationException e)
            {
                exception1 = e;
            }

            try
            {
                throw new ArgumentException("test2");
            }
            catch (ArgumentException e)
            {
                exception2 = e;
            }

            T result = CreateFrom(new AggregateException(exception1, exception2));

            Assert.Contains("test1", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("test2", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("InvalidOperationException", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ArgumentException", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("  at", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ErrorMessagePreserved()
        {
            T result = CreateFrom("error");
            Assert.Equal("error", result.ErrorMessage);
        }

        [Fact]
        public void DiagnosticsPreserved()
        {
            T result = CreateFrom("error", "diags");
            Assert.Equal("diags", result.Diagnostics);
        }
    }
}

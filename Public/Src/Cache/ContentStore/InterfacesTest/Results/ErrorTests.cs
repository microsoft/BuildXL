// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Results
{
    public class ErrorTests : TestWithOutput
    {
        public ErrorTests(ITestOutputHelper helper)
            : base(helper)
        { }

        [Fact]
        public void TestFromException()
        {
            var result = Error.FromException(GenerateException(), "My operation");
            var str = result.ToString();
            Output.WriteLine(str);

            Assert.Contains("My operation", str);
            Assert.Contains("My operation", result.ErrorMessage);
            Assert.Contains(nameof(NullReferenceException), str);
            Assert.Contains(nameof(FooBar), str);

            Assert.Contains(nameof(NullReferenceException), result.Diagnostics);
            Assert.Contains(nameof(FooBar), result.Diagnostics);
        }

        [Fact]
        public void TestFromErrorMessage()
        {
            var result = Error.FromErrorMessage("My operation", "my diagnostics");
            var str = result.ToString();
            Output.WriteLine(str);

            Assert.Contains("My operation", str);
            Assert.Contains("My operation", result.ErrorMessage);
            Assert.Contains("my diagnostics", str);

            Assert.DoesNotContain("My operation", result.Diagnostics);
            Assert.Contains("my diagnostics", result.Diagnostics);
        }

        [Fact]
        public void TestMergeWithNoExceptions()
        {
            var result1 = Error.FromErrorMessage("My operation1", "my diagnostics1");
            var result2 = Error.FromErrorMessage("My operation2", "my diagnostics2");
            var result = Error.Merge(result1, result2, ", ");

            var str = result.ToString();
            Output.WriteLine(str);

            Assert.Contains("My operation1", str);
            Assert.Contains("My operation2", str);

            Assert.Contains("My operation1", result.ErrorMessage);
            Assert.Contains("My operation2", result.ErrorMessage);

            Assert.Contains("my diagnostics1", str);
            Assert.Contains("my diagnostics2", str);

            Assert.Contains("my diagnostics1", result.Diagnostics);
            Assert.Contains("my diagnostics2", result.Diagnostics);

            Assert.Null(result.Exception);
        }

        [Fact]
        public void TestMergeWithOneException()
        {
            var result1 = Error.FromException(GenerateException(), "My operation1");
            var result2 = Error.FromErrorMessage("My operation2", "my diagnostics2");
            var result = Error.Merge(result1, result2, ", ");

            var str = result.ToString();
            Output.WriteLine(str);

            Assert.Contains("My operation1", str);
            Assert.Contains("My operation2", str);

            Assert.Contains("My operation1", result.ErrorMessage);
            Assert.Contains("My operation2", result.ErrorMessage);

            Assert.Contains("my diagnostics2", str);
            Assert.Contains(nameof(NullReferenceException), str);

            Assert.Contains("my diagnostics2", result.Diagnostics);
            Assert.Contains(nameof(NullReferenceException), result.Diagnostics);

            Assert.NotNull(result.Exception);
        }

        [Fact]
        public void TestMergeWithTwoException()
        {
            var result1 = Error.FromException(GenerateException(), "My operation1");
            var result2 = Error.FromException(GenerateException(), "My operation2");
            var result = Error.Merge(result1, result2, ", ");

            var str = result.ToString();
            Output.WriteLine(str);

            Assert.Contains("My operation1", str);
            Assert.Contains("My operation2", str);

            Assert.Contains("My operation1", result.ErrorMessage);
            Assert.Contains("My operation2", result.ErrorMessage);

            // Should be 4 occurrences of 'NullReferenceException' in the final string (one in the error message and one in the diagnostics).
            Assert.Equal(5, str.Split(new string[] { nameof(NullReferenceException) }, StringSplitOptions.RemoveEmptyEntries).Count());

            Assert.Contains(nameof(NullReferenceException), str);
            Assert.Contains(nameof(NullReferenceException), result.Diagnostics);

            Assert.NotNull(result.Exception);
            Assert.IsType(typeof(AggregateException), result.Exception);
        }

        private Exception GenerateException()
        {
            try
            {
                FooBar();
            }
            catch(NullReferenceException e)
            {
                return e;
            }

            throw null;
        }

        private void FooBar()
        {
            baz();

            void baz() => throw null;
        }
    }
}

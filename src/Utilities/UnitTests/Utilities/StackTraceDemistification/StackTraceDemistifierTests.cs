// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities.StackTraceDemistification
{
    public class StackTraceDemistifierTests : XunitBuildXLTest
    {
        private readonly ITestOutputHelper m_output;

        public StackTraceDemistifierTests(ITestOutputHelper output)
            : base(output)
        {
            m_output = output;
        }

        [Fact]
        public void DemystifyShouldNotAffectTheOriginalStackTrace()
        {
            try
            {
                SampleMethodThatThrows().Wait();
                // Tuples((o: null, s: string.Empty));
            }
            catch (Exception e)
            {
                string original = e.ToString();
                var stringDemystified = e.ToStringDemystified();

                m_output.WriteLine("Demystified: ");
                m_output.WriteLine(stringDemystified);

                m_output.WriteLine("Original: ");
                var afterDemystified = e.ToString();
                m_output.WriteLine(afterDemystified);

                Assert.Equal(original, afterDemystified);
            }
        }

        [Fact]
        public void TestAsyncStackTrace()
        {
            try
            {
                SampleMethodThatThrows().Wait();
            }
            catch (Exception e)
            {
                var stringDemystified = e.ToStringDemystified();
                m_output.WriteLine(stringDemystified);
                Assert.Contains("async Task Test", stringDemystified);
            }
        }

        [Fact]
        public void TestAsyncStackTraceWithTuple()
        {
            try
            {
                AsyncWithTuple().Wait();
            }
            catch (Exception e)
            {
                var stringDemystified = e.ToStringDemystified();
                m_output.WriteLine(stringDemystified);
                Assert.Contains("async Task<(int left, int right)>", stringDemystified);
            }
        }

        [Fact]
        public void TestMethodWithTuples()
        {
            try
            {
                Tuples((o: null, s: string.Empty));
            }
            catch (Exception e)
            {
                var stringDemystified = e.ToStringDemystified();
                m_output.WriteLine(stringDemystified);
                Assert.Contains("(int left, int right) Test", stringDemystified);
            }
        }

        private async Task SampleMethodThatThrows()
        {
            await Task.Yield();
            throw new InvalidOperationException("message");
        }

        private async Task<(int left, int right)> AsyncWithTuple()
        {
            await Task.Yield();
            throw new InvalidOperationException("message");
        }

        private (int left, int right) Tuples((object o, string s) arg)
        {
            throw new InvalidOperationException("message");
        }
    }
}

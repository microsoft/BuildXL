// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;
using BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes.Detours
{
    public sealed class OutputFilterTest
    {
        [Fact]
        public void GetErrorFilterTest()
        {
            bool enableMultiLine = true;
            Regex errRegex = new Regex(@"(?s)<error>[\\s*]*(?<ErrorMessage>.*?)[\\s*]*</error>");
            OutputFilter errorFilter = OutputFilter.GetErrorFilter(errRegex, enableMultiLine);
            const string ErrText = @"
* BEFORE *
* <error> *
* err1 *
* </error> *
* AFTER *
* <error>err2</error> * <error>err3</error> *
";
            var expectedErrOutputChunks = new[] 
            {
                @" *
* err1 *
* ",
                "err2",
                "err3"
            };

            var expectedErrOutput = string.Join(Environment.NewLine, expectedErrOutputChunks);
            XAssert.AreEqual(expectedErrOutput, errorFilter.ExtractMatches(ErrText));
        }

        [Fact]
        public void GetPipPropertyFilterTest()
        {
            bool enableMultiLine = true;
            OutputFilter propertyFilter = OutputFilter.GetPipPropertiesFilter(enableMultiLine);
            const string OutputText = @"RandomTextPipProperty_SomeProp_123456_EndPropertyOtherRandomText";
            const string ExpectedPipPropertyMatches = @"SomeProp_123456";

            XAssert.AreEqual(ExpectedPipPropertyMatches, propertyFilter.ExtractMatches(OutputText));
        }
    }
}

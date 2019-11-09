using System.Text.RegularExpressions;
using BuildXL.Processes;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Processes.Detours
{
    public sealed partial class OutputFilterTest
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
            const string ExpectedErrOutput = @" *
* err1 *
* 
err2
err3";

            XAssert.AreEqual(ExpectedErrOutput, errorFilter.ExtractMatches(ErrText));
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

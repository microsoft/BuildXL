// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretBinaryExpressions : DsTest
    {
        public InterpretBinaryExpressions(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void MultiplicationHasHigherPriorityThanAddition()
        {
            string code = @"
export const r = 1 + 2*3 - 4%2 + (5+1)%2*3;
";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.Equal(7, result);
        }

        [Fact]
        public void DivideByZero()
        {
            string code = @"
namespace M {
    const x : number = 42;
    const y : number = 0;
    export const result : number = x % y;
}";
            var diagnostic = EvaluateWithFirstError(code);

            Assert.Equal((int)LogEventId.DivideByZero, diagnostic.ErrorCode);
        }

        [Fact]
        public void CompoundAssignments()
        {
            string code = @"
namespace M {
    // Some operations are only possible in the function body!
    function test1() {
      let x = 0;
      let y = 1;
      let z = 1;
      let w = -1;

      y = y + 1;
      x += 1;
      z = z * 1;
      w -= 5;

      return [x, y, z, w];
    }
    const x : number = 42;
    const y : number = 0;
    const t1 = test1();

    export const r1 = t1[0]; // 1
    export const r2 = t1[1]; // 2
    export const r3 = t1[2]; // 1
    export const r4 = t1[3]; // -6
}";
            var result = EvaluateExpressionsWithNoErrors(code, "M.r1", "M.r2", "M.r3", "M.r4");

            Assert.Equal(1, result["M.r1"]);
            Assert.Equal(2, result["M.r2"]);
            Assert.Equal(1, result["M.r3"]);
            Assert.Equal(-6, result["M.r4"]);
        }

        [Fact]
        public void TestTruthy()
        {
            string code = @"
namespace M
{
    const x : number = undefined;
    export const b1 = x && Contract.fail('we should not get here');
    export const b2 = x || 42;
}";

            var result = EvaluateExpressionsWithNoErrors(code, "M.b1", "M.b2");

            Assert.Equal(UndefinedValue.Instance, result["M.b1"]);
            Assert.Equal(42, result["M.b2"]);
        }

        [Fact]
        public void StringAndPathAdd()
        {
            string code = @"
export const r = p`myfile.txt` + ""..other"";
";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.True(((string)result).StartsWith("p`"));
            Assert.True(((string)result).EndsWith("/myfile.txt`..other"));
        }

        [Fact]
        public void PathAndStringAdd()
        {
            string code = @"
export const r = ""other.."" + p`myfile.txt`;
";
            var result = EvaluateExpressionWithNoErrors(code, "r");

            Assert.True(((string)result).StartsWith("other..p`"));
            Assert.True(((string)result).EndsWith("/myfile.txt`"));
        }

        /*
         * On macOS the default stack size is 512kb and this test fails if the stack size limit is not adjusted prior to CoreCLR init
         * (e.g. if running the thest within a debugger / IDE). Our bxl wrapper shell scripts currently set COMPlus_DefaultStackSize to fix this.
         */
        [Fact]
        public void SuperLongBinaryExpressionShouldNotLeadToStackoverflow()
        {
            // This sample led to stack overflow when NodeWalker was recursive
            string code =
                @"const r = ""	-def:O:\\Off\\Target\\x64\\debug\\liblet_memoryapi\\x-none\\lib\\memoryLeakScopebasicImplWin.def /IMPLIB:O:\\off\\build\\x64\\debug\\liblet\\memoryapi\\leakscope\\basicimpl\\layermap\\win\\objd\\x64\\LayerMap_memoryLeakScopebasicImplWin.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	@O:\\Off\\Target\\x64\\debug\\liblet_memoryapi\\x-none\\lib\\memoryLeakScopebasicImplWin.lob"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	O:\\off\\dev\\otools\\lib\\x64\\CoreSDK\\kernel32.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	O:\\off\\dev\\otools\\lib\\x64\\CoreSDK\\ole32.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	O:\\off\\dev\\otools\\lib\\x64\\CoreSDK\\user32.lib"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\vccorlibd.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	O:\\Off\\Import\\x64\\debug\\liblet_memoryapi\\x-none\\util_olibc\\x-none\\lib\\olibc.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\msvcrtd.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\vcruntimed.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\legacy_stdio_definitions.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\legacy_stdio_wide_specifiers.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\comsuppwd.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\msvcprtd.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\concrtd.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\ucrtd.lib"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	-nodefaultlib"" + Environment.newLine() + ""	-fixed:no"" + Environment.newLine() + ""	-dynamicbase"" + Environment.newLine() + ""	-debug:none"" + Environment.newLine() + ""	/d2:-notypeopt"" + Environment.newLine() + ""	/HIGHENTROPYVA"" + Environment.newLine() + ""	-etm:no"" + Environment.newLine() + ""	-incremental"" + Environment.newLine() + ""	-ignore:4005"" + Environment.newLine() + ""	-ignore:4042"" + Environment.newLine() + ""	-ignore:4013"" + Environment.newLine() + ""	-NODEFAULTLIB"" + Environment.newLine() + ""	-verbose:incr"" + Environment.newLine() + ""	-time"" + Environment.newLine() + ""	-OPT:NOREF"" + Environment.newLine() + ""	"" + Environment.newLine() + ""	-IGNORE:4001"" + Environment.newLine() + ""	-IGNORE:4039"" + Environment.newLine() + ""	-IGNORE:4070"" + Environment.newLine() + ""	-IGNORE:4076"" + Environment.newLine() + ""	-IGNORE:4087"" + Environment.newLine() + ""	-IGNORE:4089"" + Environment.newLine() + ""	-IGNORE:4099"" + Environment.newLine() + ""	-IGNORE:4198"" + Environment.newLine() + ""	-IGNORE:4199"" + Environment.newLine() + ""	/delayload:api-ms-win-core-winrt-l1-1-0.dll"" + Environment.newLine() + ""	/delayload:api-ms-win-core-winrt-string-l1-1-0.dll"" + Environment.newLine() + ""	/delayload:api-ms-win-core-winrt-error-l1-1-0.dll"" + Environment.newLine() + ""	/delayload:api-ms-win-core-winrt-error-l1-1-1.dll"" + Environment.newLine() + ""	/delayload:bcrypt.dll"" + Environment.newLine() + ""	/delayload:credui.dll"" + Environment.newLine() + ""	/delayload:crypt32.dll"" + Environment.newLine() + ""	/delayload:dwmapi.dll"" + Environment.newLine() + ""	/delayload:imm32.dll"" + Environment.newLine() + ""	/delayload:mpr.dll"" + Environment.newLine() + ""	/delayload:msi.dll"" + Environment.newLine() + ""	/delayload:ncrypt.dll"" + Environment.newLine() + ""	/delayload:msipc.dll"" + Environment.newLine() + ""	/delayload:normaliz.dll"" + Environment.newLine() + ""	/delayload:oleacc.dll"" + Environment.newLine() + ""	/delayload:powrprof.dll"" + Environment.newLine() + ""	/delayload:psapi.dll"" + Environment.newLine() + ""	/delayload:secur32.dll"" + Environment.newLine() + ""	/delayload:shcore.dll"" + Environment.newLine() + ""	/delayload:shell32.dll"" + Environment.newLine() + ""	/delayload:shlwapi.dll"" + Environment.newLine() + ""	/delayload:UIAutomationCore.dll"" + Environment.newLine() + ""	/delayload:urlmon.dll"" + Environment.newLine() + ""	/delayload:user32.dll"" + Environment.newLine() + ""	/delayload:userenv.dll"" + Environment.newLine() + ""	/delayload:uxtheme.dll"" + Environment.newLine() + ""	/delayload:version.dll"" + Environment.newLine() + ""	/delayload:winhttp.dll"" + Environment.newLine() + ""	/delayload:wininet.dll"" + Environment.newLine() + ""	/delayload:xmllite.dll"" + Environment.newLine() + ""	/delayload:dbghelp.dll"" + Environment.newLine() + ""	d:\\CxCache\\VCCompiler.Libs.x64.14.0.23026.1\\crt\\delayimp.lib"" + Environment.newLine() + ""	-subsystem:console,6.1"" + Environment.newLine() + ""	-entry:_DllMainCRTStartup"" + Environment.newLine() + ""	-dll"" + Environment.newLine() + """";";
            var result = (string)EvaluateExpressionWithNoErrors(code, "r");

            Assert.Contains("memoryLeakScopebasicImplWin.def", result);
        }

        [Theory]
        [InlineData(true, "1 === 1")]
        [InlineData(false, "1 === 2")]

        [InlineData(true, "'1' === '1'")]

        [InlineData(true, "true === true")]
        [InlineData(false, "true === false")]

        [InlineData(true, "undefined === undefined")]

        [InlineData(false, "1 === undefined")]
        [InlineData(false, "'1' === undefined")]

        [InlineData(true, "f === f")]
        [InlineData(false, "f === g")]
        [InlineData(false, "g === f")]
        [InlineData(true, "f !== g")]
        [InlineData(true, "g !== f")]
        [InlineData(false, "f === undefined")]
        [InlineData(true, "f !== undefined")]
        public void CompareEquals(bool expectedResult, string expr)
        {
            var code = @"
function f() {};
function g() {};

export const r = " + expr + ";";
            Assert.Equal(expectedResult, (bool)EvaluateExpressionWithNoErrors(code, "r"));
        }

        [Theory]
        [InlineData("'1'", "'2'", "\"1\"", "\"2\"")]
        [InlineData("'1'", "1", "\"1\"", "number")]
        [InlineData("'1'", "true", "\"1\"", "boolean")]
        [InlineData("1", "true", "number", "boolean")]
        public void TestComparingIncomparableValuesFailsWithTypeError(string lhs, string rhs, string lhsType, string rhsType)
        {
            Build()
                .AddSpec($"export const r = {lhs} === {rhs};")
                .EvaluateWithCheckerDiagnostic(TypeScript.Net.Diagnostics.Errors.Operator_0_cannot_be_applied_to_types_1_and_2, "===", lhsType, rhsType);
        }
    }
}

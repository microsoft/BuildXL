// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.DScript;
using TypeScript.Net.UnitTests.TypeChecking;
using TypeScript.Net.UnitTests.Utils;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.TypeChecking
{
    public sealed class DecoratorTests
    {
        private readonly ITestOutputHelper m_output;

        public DecoratorTests(ITestOutputHelper output)
        {
            m_output = output;
        }

        [Theory]
        [MemberData(nameof(ToolsOptionsCases))]
        public void ToolOptionIsAllowedForThisLocation(string code, bool includeToolOption)
        {
            if (includeToolOption)
            {
                // First category of test cases:
                // if Tool.option is included then all the test cases should pass
                var diagnostics = TestWithToolOption(code);

                Assert.Empty(diagnostics);
            }
            else
            {
                // In this case all the cases should gracefully fail with the error: can't find Tool.option
                var diagnostics = TestWithoutToolOption(code);
                Assert.NotEmpty(diagnostics);
            }
        }

        public static IEnumerable<object[]> ToolsOptionsCases()
        {
            // Generate the same set of test cases with the same code for two main categories of tests:
            // when to inclde Tool.option function and when to not.
            // In this case the first set of cases should just pass,
            // and the second set should gracefully fail that the decorator is not resolved.
            return DoGetTestCases()
                .Select(s => new object[] {s, true})
                .Union(
                    DoGetTestCases()
                        .Select(s => new object[] {s, false}))
                .ToArray();

            IEnumerable<string> DoGetTestCases()
            {
                yield return
                    @"
@@Tool.option(""\hello"")
export interface I{}";
                    yield return
                    @"
namespace X {
    @@Tool.option(""\hello"")
    export interface I{}
}";
                yield return @"
namespace X.Y {
    @@Tool.option(""\hello"")
    export interface I{}
}";
                yield return @"
@@Tool.option(""\hello"")
export const x = 42;";
                yield return @"
@@Tool.option(""\hello"")
export function g(){}";
                yield return @"
@@Tool.option(""\hello"")
export const enum MyEnum {
    case1
}";
                yield return @"
export const enum MyEnum {
    @@Tool.option(""\hello"")
    case1
}";
                yield return @"
@@Tool.option(""\hello"")
export type MyType = number;";
                yield return @"
export type MyType = 
  @@Tool.option('foo')
  'foo' |
  @@Tool.option('bar')
  'bar';";
                yield return @"
@@Tool.option(""\hello"")
namespace X {}";

                yield return @"
export const x = 42;
@@Tool.option(""\hello"")
namespace X {}";

                // export declaration allows @@public decorators, so it is theoretically possible
                // to use any decorators there.
                yield return @"
export const x = 42;

@@Tool.option(""\hello"")
export {x as y}";
            }
        }

        [Fact]
        public void ParseDecoratorOnStringLiteralType()
        {
            string code = @"
export type My = 
@@foobar
'foo' |
@@another(42)
'bar';";
            var sourceFile = ParsingHelper.ParseSourceFile(code);
            var diagnostics = sourceFile.ParseDiagnostics;
            Assert.Empty(diagnostics);
        }

        [Fact]
        public void TypeCheckDecoratorOnStringLiteralType()
        {
            string code = @"
export type MyType = 
  @@Tool.option('foo')'foo';";
            var diagnostics = TestWithToolOption(code);

            Assert.Empty(diagnostics);
        }

        // Test case was added for debugging purposes
        [Fact]
        public void TypeCheckDecoratorExportDeclarations()
        {
            string code = @"
export const x = 42;

@@unknown(""\hello"")
export {x as y}";
            var diagnostics = TestWithToolOption(code);

            Assert.NotEmpty(diagnostics);
        }

        [Theory]
        [InlineData(@"
function g() {
    @@Tool.option(""\hello"")
    export const x = 42;
}")]
        public void PublicDecoratorIsNotAllowedForThisLocation(string code)
        {
            var diagnostics = TestWithToolOption(code);
            Assert.NotEmpty(diagnostics);
        }

        private List<Diagnostic> TestWithToolOption(string code)
        {
            const string toolDef = @"
namespace Annotation {
    export const annotationBody: AnnotationResult = dummyArg => dummyArg;

    export type AnnotationResult = (a: any) => any;
}

namespace Tool {
    export function option(opt: string): Annotation.AnnotationResult {
        return Annotation.annotationBody;
    }
}
";

            return TestWithoutToolOption(toolDef + code);
        }

        private List<Diagnostic> TestWithoutToolOption(string code)
        {
            var diagnostics = TypeCheckingHelper.GetSemanticDiagnostics(parsingOptions: ParsingOptions.DefaultParsingOptions, implicitReferenceModule: true, codes: code);

            foreach (var d in diagnostics)
            {
                m_output.WriteLine(d.ToString());
            }

            return diagnostics;
        }
    }
}

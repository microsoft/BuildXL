// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;
using static Test.BuildXL.FrontEnd.Core.ModuleConfigurationBuilder;

namespace Test.DScript.Ast.Interpretation
{
    public sealed class InterpretDeserializedAst : SemanticBasedTests
    {
        public InterpretDeserializedAst(ITestOutputHelper output) : base(output) { }

        public static IEnumerable<object[]> SerializationCases()
        {
            // Function declarations
            yield return new object[] {"function foo(x: number) {return x;} export const x = foo(1);", 1};
            yield return new object[] {"function foo(x: number) {return x;} export const x = foo(1);", 1};
            yield return new object[] {"function foo(x: number) {return x++;} export const x = foo(1);", 1};

            // Enums
            yield return new object[] {"const enum Foo {value = 1} export const x = Foo.value.toString();", "value"};

            // Arrays
            yield return new object[] {"export const x = [1].length;", 1 };
            yield return new object[] {"export const x = [...[1]].length;", 1 };

            // Object literals
            yield return new object[] {"export const x = {x:1}.x;", 1 };
            yield return new object[] {"export const x = {x:{a:1, b: 1, c: 1, d: 1, e: 1, f:1}}.x.a;", 1 };

            // Simple expressions
            yield return new object[] {"const y = 0; export const x = y + 1;", 1 };
            yield return new object[] {"const a = 1; export const x = a > 1 ? a : 1;", 1 };

            // Simple declarations
            yield return new object[] {"namespace Ns {export const x = 1;}; export const x = Ns.x;", 1 };
            yield return new object[] {"export const x = 1;", 1 };
            yield return new object[] {"export const x:number = 1;", 1 };
            yield return new object[] {"export const x = '1';", "1" };
        }
        
        [Theory]
        [MemberData(nameof(SerializationCases))]
        public void TestDeserializedAstWithImplicitReferenceSemantics(string code, object expectedResult)
        {
            var result = BuildWithPrelude()
                .AddSpec("spec1.dsc", code)
                .UseSerializedAst()
                .EvaluateExpressionWithNoErrors("spec1.dsc", "x");

            Assert.Equal(expectedResult, result);
        }

        [Theory]
        [MemberData(nameof(SerializationCases))]
        public void TestDeserializedAstWithExplicitReferenceSemantics(string code, object expectedResult)
        {
            var result = BuildWithPrelude()
                .AddSpec("MyModule/package.config.dsc", V1Module("MyModule"))
                .AddSpec("MyModule/spec1.dsc", code)
                .UseSerializedAst()
                .EvaluateExpressionWithNoErrors("MyModule/spec1.dsc", "x");

            Assert.Equal(expectedResult, result);
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.FrontEnd.Script.Values;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.DScriptV2
{
    public class EvaluateAmbients : SemanticBasedTests
    {
        private class EnumEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y)
            {
                var number = Convert.ToInt32(x);
                var @enum = (EnumValue)y;
                return number == @enum.Value;
            }

            public int GetHashCode(object obj)
            {
                throw new NotImplementedException();
            }
        }

        private static readonly EnumEqualityComparer s_enumComparer = new EnumEqualityComparer();

        public EvaluateAmbients(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void TestDebugWriteLineCall()
        {
            var result = BuildLegacyConfigurationWithPrelude().Spec(@"
export const x = (() => {Debug.writeLine('hello, test'); return 42;})();
").EvaluateExpressionWithNoErrors("x");

            Assert.Equal(42, result);
        }

        [Fact]
        public void TestAmbientEnum()
        {
            var result = BuildLegacyConfigurationWithPrelude().Spec(@"
const enum Bar {x = 1}
export const r1 = Bar.x;
const e1 = ArtifactKind.output; // 2
export const r = e1;
").EvaluateExpressionsWithNoErrors("r", "r1");
            
            Assert.Equal("1", result["r1"], s_enumComparer);
            Assert.Equal("2", result["r"], s_enumComparer);
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using System.Linq;

namespace Test.DScript.Ast.Interpretation.Ambients
{
    public class AmbientValueCacheTestsWithParallelEvaluation : DsTest
    {
        public AmbientValueCacheTestsWithParallelEvaluation(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override int DegreeOfParallelism => 30;

        [Fact]
        public void TestCycle()
        {
            var num = 50;
            var spec = @$"
const key = 'hello';
namespace N0 {{ export const x = _PreludeAmbientHack_ValueCache.getOrAdd(key, () => N{num}.x); }}
"           + string.Join(
                Environment.NewLine,
                Enumerable
                    .Range(start: 1, count: num)
                    .Select(i => $"namespace N{i} {{ export const x = _PreludeAmbientHack_ValueCache.getOrAdd(key, () => N{i-1}.x); }}"));

            Build()
                .AddSpec(spec)
                .EvaluateWithDiagnosticId(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.Cycle, "N0.x");
        }

        [Fact]
        public void TestGetOrAddWithStateAvoidsCycles()
        {
            var value = 42;
            var spec = @$"
const key = 'hello';

namespace N0 {{ export const x = _PreludeAmbientHack_ValueCache.getOrAddWithState(key, N1.x, (s) => 1231); }}
namespace N1 {{ export const x = _PreludeAmbientHack_ValueCache.getOrAddWithState(key, N2.x, (s) => 2342); }}
namespace N2 {{ export const x = _PreludeAmbientHack_ValueCache.getOrAddWithState(key, N.x, (s) => 2390); }}
namespace N {{ export const x = _PreludeAmbientHack_ValueCache.getOrAdd(key, () => {value}); }}
";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionsWithNoErrors("N.x", "N0.x", "N1.x", "N2.x");
            XAssert.ArrayEqual(
                new[] { value, value, value, value },
                new[] { result["N.x"], result["N0.x"], result["N1.x"], result["N2.x"] }.Cast<int>().ToArray());
        }
    }

    public class AmbientValueCacheTests : DsTest
    {
        public AmbientValueCacheTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override int DegreeOfParallelism => 1;

        [Fact]
        public void TestCycle()
        {
            var spec = @"
const key = 'hello';
namespace N0 {
  export const x = _PreludeAmbientHack_ValueCache.getOrAdd(key, () => N1.x);
}

namespace N1 {
  export const x = _PreludeAmbientHack_ValueCache.getOrAdd(key, () => N0.x);
}
";
            Build()
                .AddSpec(spec)
                .EvaluateWithDiagnosticId(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.Cycle, "N0.x");
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("[1, 2, 3][0]", 1)]
        public void TestGetOrAddWithState(string stateExpr, int stateValue)
        {
            var a = 10;
            var b = 100;
            var spec = @$"
const a = {a};
const xS = _PreludeAmbientHack_ValueCache.getOrAddWithState('1', {stateExpr}, (s) => s);
const xSA = _PreludeAmbientHack_ValueCache.getOrAddWithState('2', {stateExpr}, (s) => s + a);
const xSB = (() => {{ 
    let b = {b};
    return _PreludeAmbientHack_ValueCache.getOrAddWithState('3', {stateExpr}, (s) => s + b);
}})();
const xSAB = (() => {{ 
    let b = {b};
    return _PreludeAmbientHack_ValueCache.getOrAddWithState('4', {stateExpr}, (s) => s + a + b);
}})();";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionsWithNoErrors("xS", "xSA", "xSB", "xSAB");
            XAssert.ArrayEqual(
                new[] { stateValue, stateValue + a, stateValue + b, stateValue + a + b },
                new[] { result["xS"], result["xSA"], result["xSB"], result["xSAB"] }.Cast<int>().ToArray());
        }


        [Fact]
        public void TestGetOrAddWithOrWithoutStateUseTheSameCache()
        {
            var spec = @"
const key = 'hello';
const x = _PreludeAmbientHack_ValueCache.getOrAdd(key, () => 42);
const y = _PreludeAmbientHack_ValueCache.getOrAddWithState(key, 52, (s) => s);
";
            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionsWithNoErrors("x", "y");
            XAssert.ArrayEqual(
                new[] { 42, 42 },
                new[] { result["x"], result["y"] }.Cast<int>().ToArray());
        }

        [Fact]
        public void TestApi()
        {
            var spec = @"
const world1 = {hello: 'world 1'};
const world2 = {hello: 'world 2'};
const f = _PreludeAmbientHack_ValueCache.getOrAdd('hello', () => world1);
const g = _PreludeAmbientHack_ValueCache.getOrAdd('hello', () => world2);

const assertFValue = f.hello === 'world 1';
const assertGValue = g.hello === 'world 1';

const assertFIsNot1 = f !== world1;
const assertGIsNot1 = g !== world1;
const assertGIsNot2 = g !== world2;
const assertFIsNotG = f !== g;
";

            var result = Build()
                .AddSpec(spec)
                .EvaluateExpressionsWithNoErrors("assertFValue", "assertGValue", "assertFIsNot1", "assertGIsNot1", "assertFIsNotG");
            Assert.Equal(true, result["assertFValue"]);
            Assert.Equal(true, result["assertGValue"]);
            Assert.Equal(true, result["assertFIsNot1"]);
            Assert.Equal(true, result["assertGIsNot1"]);
            Assert.Equal(true, result["assertFIsNotG"]);
        }

        [Theory]
        [InlineData(true, "'abc'", "'abc'")]
        [InlineData(true, "'abc'", "'ab'+'c'")]
        [InlineData(true, "3", "1+2")]
        [InlineData(false, "true", "false")]
        [InlineData(false, "true", "1")]
        [InlineData(false, "false", "0")]
        [InlineData(false, "f`a.txt`", "p`a.txt`")]
        [InlineData(false, "d`a.txt`", "p`a.txt`")]
        public void CompareHash(bool expectEquality, string left, string right)
        {
            var result = Build()
                .AddSpec($@"
const objLeft = {left};
const objRight = {right};
")
                .EvaluateExpressionsWithNoErrors("objLeft", "objRight");

            var leftHash = Hash(result["objLeft"]);
            var rightHash = Hash(result["objRight"]);

            if (expectEquality)
            {
                Assert.Equal(leftHash, rightHash);
            }
            else
            {
                Assert.NotEqual(leftHash, rightHash);
            }
        }

        [Theory]
        [InlineData(true, "'abc'")]
        [InlineData(true, "''")]
        [InlineData(true, "undefined")]
        [InlineData(true, "1")]
        [InlineData(true, "[]")]
        [InlineData(false, "[1]")]
        [InlineData(false, "{}")]
        [InlineData(false, "{x:1}")]
        [InlineData(true, "a`atom`")]
        [InlineData(true, "p`path`")]
        [InlineData(true, "d`dir`")]
        [InlineData(true, "f`file.txt`")]
        [InlineData(true, "r`relative`")]
        [InlineData(true, "r`.`")]
        [InlineData(false, "Map.empty<string, string>().add('a', '1')")]
        [InlineData(false, "Set.empty<string>().add('a')")]
        public void TestClone(bool expectEquality, string expression)
        {
            var result = Build()
                .AddSpec($@"
const obj = {expression};
")
                .EvaluateExpressionWithNoErrors("obj");

            var hash = Hash(result);
            var clone = AmbientValueCache.DeepCloneValue(new EvaluationResult(result));
            var hashClone = Hash(clone);

            Assert.Equal(hash, hashClone);
            if (expectEquality)
            {
                XAssert.AreEqual(result, clone.Value, "Expect the cloned values to be equal to the original.");
            }
            else
            {
                XAssert.AreNotEqual(result, clone.Value, "Expected the cloned value to be a different value.");
            }
        }

        private Fingerprint Hash(object obj)
        {
            return Hash(new EvaluationResult(obj));
        }

        private Fingerprint Hash(EvaluationResult obj)
        {
            var helper = new HashingHelper(PathTable, recordFingerprintString: false);
            if (!AmbientValueCache.TryHashValue(obj, helper))
            {
                Assert.False(true, "Expected to successfully hash the value");
            }

            return helper.GenerateHash();
        }
    }
}

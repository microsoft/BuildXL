// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Engine.Cache;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Ambients;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using BuildXL.Pips.Operations;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation.Ambients
{
    public class AmbientValueCacheTests : DsTest
    {
        public AmbientValueCacheTests(ITestOutputHelper output)
                   : base(output)
        {
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

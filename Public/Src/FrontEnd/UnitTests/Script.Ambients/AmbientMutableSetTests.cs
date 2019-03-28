// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.TestUtilities.Xunit;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;
using DsMutableSet = BuildXL.FrontEnd.Script.Ambients.Set.MutableSet;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientMutableMutableSetTests : SemanticBasedTests
    {
        public AmbientMutableMutableSetTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void MutableSetShouldPreserveTheOrder()
        {
            var spec = @"
function cloneArray<T>(...items: T[]): T[] {
  let ms = MutableSet.empty<T>();
  ms.add(...items);
  return ms.toArray();
}

export const r = cloneArray(10, 7, 3, 2, 12, 34, 1, 0, -2, -4, 42);
";
            var result = BuildWithPrelude()
                .AddSpec(spec)
                .EvaluateExpressionWithNoErrors("r");

            Assert.True(ArrayLiteralEqualityComparer.AreEqual(new int[] {10, 7, 3, 2, 12, 34, 1, 0, -2, -4, 42}, result));
        }

        [Fact]
        public void TestMutableSet()
        {
            var spec = @"
namespace M {
    export const p = 'a/path';
    export const q = 'b/path';
    export const x = <Set<string>>MutableSet.empty().add(p);
    export const y = <any>x.add(q);
    export const z = 'z/path';
    
}";
            var result = BuildWithPrelude()
                        .AddSpec(spec)
                        .EvaluateExpressionsWithNoErrors("M.x", "M.y", "M.p", "M.q", "M.z");

            var mx = (DsMutableSet)result["M.x"];
            var my = (DsMutableSet)result["M.y"];

            // List is mutable, x and y
            XAssert.AreEqual(mx, my, "x and y should point to the same instance of mutable set.");

            var p = EvaluationResult.Create(result["M.p"]);
            var q = EvaluationResult.Create(result["M.q"]);
            var z = EvaluationResult.Create(result["M.z"]);
            XAssert.IsTrue(mx.Contains(p), "Set should contains 'p'");
            XAssert.IsTrue(mx.Contains(q), "Set should contains 'q'");
            XAssert.IsFalse(mx.Contains(z), "Set should not contains 'z'");
        }

        [Fact]
        public void TestMutableSetAddUndefined()
        {
            var spec = @"
namespace M {
    export const p = 'a/path';
    export const q = undefined;
    export const y = <any>MutableSet.empty().add(p).add(q);
    
}";
            var result = BuildWithPrelude()
                .AddSpec(spec)
                .EvaluateWithFirstError("M.y");

            Assert.Equal((int)LogEventId.UndefinedSetItem, result.ErrorCode);
        }

        [Fact]
        public void TestMutableSetAddRange()
        {
            var spec = @"
namespace M {
    export const p1 = 'a/path';
    export const p2 = 'b/path';
    export const p3 = 'c/path';
    export const x = <any>MutableSet.empty().add(p1, p2, p1, p3);
    
}";
            var result = BuildWithPrelude().AddSpec(spec).EvaluateExpressionsWithNoErrors("M.x", "M.p1", "M.p2", "M.p3");

            var mx = result.Values[0] as DsMutableSet;
            Assert.NotNull(mx);

            CheckMutableSet(mx, new[] { result.Values[1], result.Values[2], result.Values[3] });
        }

        [Fact]
        public void TestMutableSetContains()
        {
            var spec = @"
    const p = 'a/path';
    const q = 'b/path';

    function checkContains() {
        const x = MutableSet.empty().add(p);

        const xContainsQ = x.contains(q); // Should be false
        const y = x.add(q);

        const xContainsQ2 = x.contains(q); // Should be true
    
        const yContainsP = y.contains(p); // should be true

        return [xContainsQ, xContainsQ2, yContainsP];
    }

    const r = checkContains();
    export const xContainsQ = r[0]; // should be true
    export const xContainsQ2 = r[1]; // should be true
    export const yContainsP = r[2]; // should be true;
    ";
            var result = BuildWithPrelude().AddSpec(spec).EvaluateExpressionsWithNoErrors("xContainsQ", "xContainsQ2", "yContainsP");

            Assert.Equal(false, result["xContainsQ"]);
            Assert.Equal(true, result["xContainsQ2"]);
            Assert.Equal(true, result["yContainsP"]);
        }

        [Fact]
        public void TestMutableSetRemove()
        {
            var spec = @"
namespace M {
    export const p1 = 'a/path';
    export const p2 = 'b/path';
    export const p3 = 'c/path';
    
    function fx() {
        const x = MutableSet.empty().add(p1, p2, p3, p1);
        const y = x.remove(p2);
        return x;
    }

    export const x = <any>fx();
}";
            var result = BuildWithPrelude().AddSpec(spec).EvaluateExpressionsWithNoErrors("M.x", "M.p1", "M.p2", "M.p3");

            var mx = result["M.x"] as DsMutableSet;
            Assert.NotNull(mx);

            var mp1 = EvaluationResult.Create(result["M.p1"]);
            var mp2 = EvaluationResult.Create(result["M.p2"]);
            var mp3 = EvaluationResult.Create(result["M.p3"]);

            XAssert.IsTrue(mx.Contains(mp1));
            XAssert.IsFalse(mx.Contains(mp2));
            XAssert.IsTrue(mx.Contains(mp3));
        }

        [Fact]
        public void TestMutableSetRemoveRange()
        {
            var spec = @"
namespace M {
    export const p1 = 'a/path';
    export const p2 = 'b/path';
    export const p3 = 'c/path';
    
    export const x = <any>MutableSet.empty().add(p1, p2, p3, p1).remove(p2, p1);
}";
            var result = BuildWithPrelude().AddSpec(spec).EvaluateExpressionsWithNoErrors("M.x", "M.p1", "M.p2", "M.p3");

            var mx = result["M.x"] as DsMutableSet;
            Assert.NotNull(mx);

            var mp1 = EvaluationResult.Create(result["M.p1"]);
            var mp2 = EvaluationResult.Create(result["M.p2"]);
            var mp3 = EvaluationResult.Create(result["M.p3"]);

            XAssert.IsFalse(mx.Contains(mp1));
            XAssert.IsFalse(mx.Contains(mp2));
            XAssert.IsTrue(mx.Contains(mp3));
        }

        [Fact]
        public void TestMutableSetToArray()
        {
            var spec = @"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = <Set<string>>MutableSet.empty().add(p1, p2, p3, p1);
    const xArray = x.toArray();
    export const y = <any>MutableSet.empty().add(...xArray);
}";
            var result = BuildWithPrelude().AddSpec(spec).EvaluateExpressionsWithNoErrors("M.x", "M.y");

            var mx = result.Values[0] as DsMutableSet;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsMutableSet;
            Assert.NotNull(my);

            // To make ReSharper happy.
            Contract.Assume(my != null);

            CheckMutableSet(mx, my.ToObjectsArray());
        }

        [Fact]
        public void TestMutableSetUnion()
        {
            var spec = @"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';

    function fxy() {
        const x = MutableSet.empty().add(p1, p1, p2, p1);
        const y = MutableSet.empty().add(p2, p3, p3);
        const xy = x.union(y);

        return xy;
    }

    function fz(){
        const z = MutableSet.empty().add(p3, p2, p1);
        return z;
    }

    export const xy = <any>fxy();
    export const z = <any>fz();

}";
            var result = BuildWithPrelude().AddSpec(spec).EvaluateExpressionsWithNoErrors("M.xy", "M.z");

            var mxy = result.Values[0] as DsMutableSet;
            Assert.NotNull(mxy);

            var mz = result.Values[1] as DsMutableSet;
            Assert.NotNull(mz);

            // To make ReSharper happy.
            Contract.Assume(mz != null);

            CheckMutableSet(mxy, mz.ToObjectsArray());
        }

        [Fact]
        public void TestMutableSetCount()
        {
            var spec = @"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = <any>MutableSet.empty().add(p1, p2, p2, p1).count();
}";
            var result = BuildWithPrelude().AddSpec(spec).EvaluateExpressionsWithNoErrors("M.x");

            Assert.Equal(2, result.Values[0]);
        }

        [Fact]
        public void MutableSetIsProhibitedOnTopLevelWithImplicitType()
        {
            var result = BuildWithPrelude().AddSpec(@"
const x = MutableSet.empty<number>();").EvaluateWithFirstError();
            Assert.Equal((int)LogEventId.NoMutableDeclarationsAtTopLevel, result.ErrorCode);
        }

        [Fact]
        public void MutableSetIsProhibitedOnTopLevelWithExplicitType()
        {
            var result = BuildWithPrelude().AddSpec(@"
const x:MutableSet<number> = undefined;").EvaluateWithFirstError();
            Assert.Equal((int)LogEventId.NoMutableDeclarationsAtTopLevel, result.ErrorCode);
        }

        [Fact]
        public void MutableSetIsProhibitedOnTopLevelWithExplicitTypeAndTwoVariables()
        {
            var result = BuildWithPrelude().AddSpec(@"
const x:string = '', y: MutableSet<string> = undefined;").EvaluateWithFirstError();
            Assert.Equal((int)LogEventId.NoMutableDeclarationsAtTopLevel, result.ErrorCode);
        }

        [Fact]
        public void MutableSetIsProhibitedForPublicFunctions()
        {
            var result = BuildWithPrelude().AddSpec(@"
export function foo(): MutableSet<number> {return undefined;}").EvaluateWithFirstError();
            Assert.Equal((int)LogEventId.NoMutableDeclarationsAtExposedFunctions, result.ErrorCode);
        }

        [Fact]
        public void MutableSetArrayIsProhibitedForPublicFunctions()
        {
            var result = BuildWithPrelude().AddSpec(@"
export function foo(): MutableSet<number>[] {return undefined;}").EvaluateWithFirstError();
            Assert.Equal((int)LogEventId.NoMutableDeclarationsAtExposedFunctions, result.ErrorCode);
        }

        [Fact]
        public void MutableSetArrayIsProhibitedForPublicFunctionsAsArgs()
        {
            var result = BuildWithPrelude().AddSpec(@"
export function foo(m: MutableSet<number>) {return undefined;}").EvaluateWithFirstError();
            Assert.Equal((int)LogEventId.NoMutableDeclarationsAtExposedFunctions, result.ErrorCode);
        }

        [Fact]
        public void NonPublicFunctionsCanReturnMutableTypes()
        {
            var result = BuildWithPrelude().AddSpec(@"
function foo(): MutableSet<number> {return MutableSet.empty<number>();}
export const x = foo().count();").EvaluateExpressionWithNoErrors("x");
            Assert.Equal(0, result);
        }

        [Fact]
        public void MutableSetIsProhibitedForPublicFunctionsWithTypeInference()
        {
            var result = BuildWithPrelude().AddSpec(@"
export function foo() {return MutableSet.empty<number>();}").EvaluateWithFirstError();
            Assert.Equal((int)LogEventId.NoMutableDeclarationsAtExposedFunctions, result.ErrorCode);
        }

        private static void CheckMutableSet(DsMutableSet mutableSet, IEnumerable<object> items, bool exactMatch = true, IEnumerable<object> notExistItems = null)
        {
            Contract.Requires(mutableSet != null);
            Contract.Requires(items != null);

            var itemsToo = items as object[] ?? items.ToArray();

            foreach (var item in itemsToo)
            {
                Assert.True(mutableSet.Contains(EvaluationResult.Create(item)));
            }

            if (exactMatch)
            {
                Assert.Equal(itemsToo.Length, mutableSet.Count);
            }

            if (notExistItems != null)
            {
                foreach (var item in notExistItems)
                {
                    Assert.False(mutableSet.Contains(EvaluationResult.Create(item)));
                }
            }
        }
    }

    internal static class MutableSetExtensions
    {
        public static object[] ToObjectsArray(this DsMutableSet set)
        {
            return set.ToArray().Select(v => v.Value).ToArray();
        }
    }
}

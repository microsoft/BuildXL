// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Ambients.Set;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using DsSet = BuildXL.FrontEnd.Script.Ambients.Set.OrderedSet;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientSetTests : DsTest
    {
        public AmbientSetTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestSet()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p = 'a/path';
    export const q = 'b/path';
    export const x = Set.empty().add(p);
    export const y = x.add(q);
    
}", new[] {"M.x", "M.y", "M.p", "M.q"});

            result.ExpectNoError();
            result.ExpectValues(count: 4);

            var mx = result.Values[0] as DsSet;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsSet;
            Assert.NotNull(my);

            var mp = result.Values[2];
            var mq = result.Values[3];
            CheckSet(mx, new[] {mp}, notExistItems: new[] {mq});
            CheckSet(my, new[] {mp, mq});
        }

        [Theory]
        [InlineData(@"Set.create()", new object[0])]
        [InlineData(@"Set.create(""hello"")", new[] { "hello" })]
        [InlineData(@"Set.create(...[""hello"", ""world""])", new[] { "hello", "world" })]
        [InlineData(@"Set.create(""hello"").add(""world"")", new[] { "hello", "world" })]
        [InlineData(@"Set.create(1,4,8).add(19)", new object[] { 1, 4, 8, 19 })]
        public void TestSetCreate(string expression, object[] expectedValuesInSet)
        {
            var result = EvaluateSpec(I($"export const set = {expression};"), new[] { "set" });
            result.ExpectNoError();
            result.ExpectValues(count: 1);

            var set = result.Values[0] as DsSet;
            Assert.Equal(expectedValuesInSet, set.ToObjectsArray());
        }

        [Fact]
        public void TestSetAddUndefined()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p = 'a/path';
    export const q = undefined;
    export const x = Set.empty().add(p);
    export const y = x.add(q);
    
}", new[] {"M.y"});

            result.ExpectErrors(count: 1);
            result.ExpectValues(count: 1);

            Assert.Equal(ErrorValue.Instance, result.Values[0]);
            result.ExpectErrorCode((int)LogEventId.UndefinedSetItem, count: 1);
        }

        [Fact]
        public void TestSetAddRange()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p1 = 'a/path';
    export const p2 = 'b/path';
    export const p3 = 'c/path';
    export const x = Set.empty().add(p1, p2, p1, p3);
    
}", new[] {"M.x", "M.p1", "M.p2", "M.p3"});

            result.ExpectNoError();
            result.ExpectValues(count: 4);

            var mx = result.Values[0] as DsSet;
            Assert.NotNull(mx);

            CheckSet(mx, new[] { result.Values[1], result.Values[2], result.Values[3] });
        }

        [Fact]
        public void TestSetContains()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p = 'a/path';
    const q = 'b/path';
    const x = Set.empty().add(p);
    const y = x.add(q);
    export const checkX = x.contains(q);
    export const checkY = y.contains(p);
    
}", new[] {"M.checkX", "M.checkY"});

            result.ExpectNoError();
            result.ExpectValues(count: 2);

            Assert.Equal(false, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
        }

        [Fact]
        public void TestSetRemove()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p1 = 'a/path';
    export const p2 = 'b/path';
    export const p3 = 'c/path';
    export const x = Set.empty().add(p1, p2, p3, p1);
    export const y = x.remove(p2);
}", new[] {"M.x", "M.y", "M.p1", "M.p2", "M.p3"});

            result.ExpectNoError();
            result.ExpectValues(count: 5);

            var mx = result.Values[0] as DsSet;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsSet;
            Assert.NotNull(my);

            var mp1 = result.Values[2];
            var mp2 = result.Values[3];
            var mp3 = result.Values[4];

            CheckSet(mx, new[] {mp1, mp2, mp3});
            CheckSet(my, new[] {mp1, mp3}, notExistItems: new[] {mp2});
        }

        [Fact]
        public void TestSetRemoveRange()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p1 = 'a/path';
    export const p2 = 'b/path';
    export const p3 = 'c/path';
    export const x = Set.empty().add(p1, p2, p3, p1);
    export const y = x.remove(p2, p1);
}", new[] {"M.x", "M.y", "M.p1", "M.p2", "M.p3"});

            result.ExpectNoError();
            result.ExpectValues(count: 5);

            var mx = result.Values[0] as DsSet;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsSet;
            Assert.NotNull(my);

            var mp1 = result.Values[2];
            var mp2 = result.Values[3];
            var mp3 = result.Values[4];

            CheckSet(mx, new[] {mp1, mp2, mp3});
            CheckSet(my, new[] {mp3}, notExistItems: new[] {mp1, mp2});
        }

        [Fact]
        public void TestSetToArray()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Set.empty().add(p1, p2, p3, p1);
    const xArray = x.toArray();
    export const y = Set.empty().add(...xArray);
}", new[] {"M.x", "M.y"});

            result.ExpectNoError();
            result.ExpectValues(count: 2);

            var mx = result.Values[0] as DsSet;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsSet;
            Assert.NotNull(my);

            // To make ReSharper happy.
            Contract.Assume(my != null);

            CheckSet(mx, my.ToObjectsArray());
        }

        [Fact]
        public void TestSetForEach()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = ""p1"";
    const p2 = ""p2"";
    const p3 = ""p3"";
    export const x = Set.empty().add(p1, p2, p3, p1);
    const w = x.forEach(item => {return item + ""_foo""; });
    export const y = Set.empty().add(...w);
}", new[] {"M.x", "M.y"});

            result.ExpectNoError();
            result.ExpectValues(count: 2);

            var mx = result.Values[0] as DsSet;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsSet;
            Assert.NotNull(my);

            // To make ReSharper happy.
            Contract.Assume(my != null);
            Contract.Assume(mx != null);

            CheckSet(my, mx.ToArray().Select(item => item.Value + "_foo"));
        }

        [Fact]
        public void TestSetUnion()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Set.empty().add(p1, p1, p2, p1);
    export const y = Set.empty().add(p2, p3, p3);
    export const z = Set.empty().add(p3, p2, p1);
    export const xy = x.union(y);
}", new[] {"M.xy", "M.z"});

            result.ExpectNoError();
            result.ExpectValues(count: 2);

            var mxy = result.Values[0] as DsSet;
            Assert.NotNull(mxy);

            var mz = result.Values[1] as DsSet;
            Assert.NotNull(mz);

            // To make ReSharper happy.
            Contract.Assume(mz != null);

            CheckSet(mxy, mz.ToObjectsArray());
        }

        [Fact]
        public void TestSetIntersect()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Set.empty().add(p1, p1, p2, p1);
    export const y = Set.empty().add(p2, p3, p1);
    export const z = Set.empty().add(p2, p1);
    export const xy = x.intersect(y);
}", new[] {"M.xy", "M.z"});

            result.ExpectNoError();
            result.ExpectValues(count: 2);

            var mxy = result.Values[0] as DsSet;
            Assert.NotNull(mxy);

            var mz = result.Values[1] as DsSet;
            Assert.NotNull(mz);

            // To make ReSharper happy.
            Contract.Assume(mz != null);

            CheckSet(mxy, mz.ToObjectsArray());
        }

        [Fact]
        public void TestSetExcept()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Set.empty().add(p1, p1, p2, p1);
    export const y = Set.empty().add(p2, p3, p1);
    export const z = Set.empty();
    export const xy = x.except(y);
}", new[] {"M.xy", "M.z"});

            result.ExpectNoError();
            result.ExpectValues(count: 2);

            var mxy = result.Values[0] as DsSet;
            Assert.NotNull(mxy);

            var mz = result.Values[1] as DsSet;
            Assert.NotNull(mz);

            // To make ReSharper happy.
            Contract.Assume(mz != null);

            CheckSet(mxy, mz.ToObjectsArray());
        }

        [Fact]
        public void TestSetSubsetSuperset()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Set.empty().add(p1, p2);
    export const y = Set.empty().add(p2, p3, p1);
    export const z = Set.empty().add(p1);
    
    export const zIsSubsetOfx = z.isSubsetOf(x);
    export const zIsProperSubsetOfx = z.isProperSubsetOf(x);

    export const xIsSupersetOfz = x.isSupersetOf(z);
    export const xIsProperSubsetOfz = x.isProperSupersetOf(z);

    const xy = x.intersect(y);

    export const xyIsSubSetOfx = xy.isSubsetOf(x);
    export const xyIsProperSubSetOfx = xy.isProperSubsetOf(x);

}", new[] {"M.zIsSubsetOfx", "M.zIsProperSubsetOfx", "M.xIsSupersetOfz", "M.xIsProperSubsetOfz", "M.xyIsSubSetOfx", "M.xyIsProperSubSetOfx"});

            result.ExpectNoError();
            result.ExpectValues(count: 6);

            Assert.Equal(true, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
            Assert.Equal(true, result.Values[2]);
            Assert.Equal(true, result.Values[3]);
            Assert.Equal(true, result.Values[4]);
            Assert.Equal(false, result.Values[5]);
        }

        [Fact]
        public void TestSetCount()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Set.empty().add(p1, p2, p2, p1).count();
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(count: 1);

            Assert.Equal(2, result.Values[0]);
        }

        [Fact]
        public void TestSetOrdering()
        {
            var result = EvaluateSpec(@"
namespace M {
    const a = 'a/path';
    const b = 'b/path';
    const c = 'c/path';
    const d = 'd/path';
    const e = 'e/path';
    const f = 'f/path';

    const ps1 = [a, b, c];
    const ps2 = [d, e, f];
    const ps3 = [a, b];
    const ps4 = [c, d];
    const ps5 = [a, c];
    const ps6 = [a, b, e, f];

    const ps1Dup = [...ps1, ...ps3];
    const ps2Add = [...ps2, ...ps3];

    const x = Set.empty().add(...ps1Dup);
    const y = Set.empty().add(...ps2Add);
    const z = Set.empty().add(...ps4);

    const xUy = x.union(y);
    const xIy = x.intersect(y);
    const xUyRemRange = xUy.remove(...ps4);
    const xRemB = x.remove('b/path');
    const xUyEz = xUy.except(z);
        
    export const checkX = check(x.toArray(), ps1);
    export const checkXUY = check(xUy.toArray(), [...ps1, ...ps2]);
    export const checkXIY = check(xIy.toArray(), ps3);
    export const checkXUYRemRange = check(xUyRemRange.toArray(), ps6);
    export const checkXRemB = check(xRemB.toArray(), ps5);
    export const checkXUYEZ = check(xUyEz.toArray(), ps6);
    
    function check(array1 : any[], array2 : any[]) {
        if (array1.length !== array2.length) return false;
        return array1.map((e, i) => e === array2[i]).all(b => b);
    }
}", new[] { "M.checkX", "M.checkXUY", "M.checkXIY", "M.checkXUYRemRange", "M.checkXRemB", "M.checkXUYEZ" });

            result.ExpectNoError();
            result.ExpectValues(count: 6);

            Assert.Equal(true, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
            Assert.Equal(true, result.Values[2]);
            Assert.Equal(true, result.Values[3]);
            Assert.Equal(true, result.Values[4]);
            Assert.Equal(true, result.Values[5]);
        }

        private static void CheckSet(DsSet set, IEnumerable<object> items, bool exactMatch = true, IEnumerable<object> notExistItems = null)
        {
            Contract.Requires(set != null);
            Contract.Requires(items != null);

            var itemsToo = items as object[] ?? items.ToArray();

            foreach (var item in itemsToo)
            {
                Assert.True(set.Contains(EvaluationResult.Create(item)));
            }

            if (exactMatch)
            {
                Assert.Equal(set.Count, itemsToo.Length);
            }

            if (notExistItems != null)
            {
                foreach (var item in notExistItems)
                {
                    Assert.False(set.Contains(EvaluationResult.Create(item)));
                }
            }
        }
    }

    internal static class OrderedSetExtensions
    {
        public static object[] ToObjectsArray(this OrderedSet set)
        {
            return set.ToArray().Select(v => v.Value).ToArray();
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using DsMap = BuildXL.FrontEnd.Script.Ambients.Map.OrderedMap;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientMapTests : DsTest
    {
        public AmbientMapTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TestMapAdd()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p = 'a/path';
    export const q = 'b/path';
    export const x = Map.empty().add(p, 42);
    export const y = x.add(q, 42);
    
}", new[] {"M.x", "M.y", "M.p", "M.q"});

            result.ExpectNoError();
            result.ExpectValues(4);

            var mx = result.Values[0] as DsMap;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsMap;
            Assert.NotNull(my);

            var mp = result.Values[2];
            var mq = result.Values[3];

            CheckMap(mx, new[] {Mapping(mp, 42)}, notExistKeys: new[] {mq});
            CheckMap(my, new[] {Mapping(mp, 42), Mapping(mq, 42)});
        }

        [Fact]
        public void TestMapAddUndefined()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p = 'a/path';
    export const q = undefined;
    export const x = Map.empty().add(p, 42);
    export const y = x.add(q, 42);
    
}", new[] { "M.y" });

            result.ExpectErrors(count: 1);
            result.ExpectValues(1);

            Assert.Equal(ErrorValue.Instance, result.Values[0]);
            result.ExpectErrorCode((int)LogEventId.UndefinedMapKey, count: 1);
        }

        [Fact]
        public void TestMapAddRange()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p1 = 'a/path';
    export const p2 = 'b/path';
    export const p3 = 'c/path';
    export const x = Map.empty().addRange([p1, 1], [p2, 2], [p3, 3], [p1, 42]);
    
}", new[] {"M.x", "M.p1", "M.p2", "M.p3"});

            result.ExpectNoError();
            result.ExpectValues(4);

            var mx = result.Values[0] as DsMap;
            Assert.NotNull(mx);

            CheckMap(mx, new[] {Mapping(result.Values[1], 42), Mapping(result.Values[2], 2), Mapping(result.Values[3], 3)});
        }

        [Fact]
        public void TestMapAddRangeInvalidKeyValuePair()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p1: any = 'a/path';
    export const p2: any = 'b/path';
    export const p3: any = 'c/path';
    export const x = Map.empty().addRange(p1, p2, p3);
    export const y = Map.empty().addRange([p1, 1], [p2, 2, 4], [p3, 3]);
    
}", new[] { "M.x", "M.y" });

            result.ExpectErrors(count: 2);
            result.ExpectValues(2);
            Assert.Equal(ErrorValue.Instance, result.Values[0]);
            Assert.Equal(ErrorValue.Instance, result.Values[1]);
            result.ExpectErrorCode((int)LogEventId.UnexpectedValueTypeOnConversion, count: 1);
            result.ExpectErrorCode((int)LogEventId.InvalidKeyValueMap, count: 1);
        }

        [Fact]
        public void TestMapContainsKey()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p = 'a/path';
    const q = 'b/path';
    const x = Map.empty().add(p, 42);
    const y = x.add(q, 42);
    export const checkX = x.containsKey(q);
    export const checkY = y.containsKey(p);
    
}", new[] {"M.checkX", "M.checkY"});

            result.ExpectNoError();
            result.ExpectValues(2);

            Assert.Equal(false, result.Values[0]);
            Assert.Equal(true, result.Values[1]);
        }

        [Fact]
        public void TestMapGet()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    const x = Map.empty().addRange([p1, 1], [p2, 2], [p1, 42]);
    export const getP1 = x.get(p1);
    export const getP3 = x.get(p3);
}", new[] {"M.getP1", "M.getP3"});

            result.ExpectNoError();
            result.ExpectValues(2);

            Assert.Equal(42, result.Values[0]);
            Assert.Equal(UndefinedValue.Instance, result.Values[1]);
        }

        [Fact]
        public void TestMapRemove()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p1 = 'a/path';
    export const p2 = 'b/path';
    export const p3 = 'c/path';
    export const x = Map.empty().addRange([p1, 1], [p2, 2], [p3, 3], [p1, 42]);
    export const y = x.remove(p2);
}", new[] {"M.x", "M.y", "M.p1", "M.p2", "M.p3"});

            result.ExpectNoError();
            result.ExpectValues(5);

            var mx = result.Values[0] as DsMap;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsMap;
            Assert.NotNull(my);

            var mp1 = result.Values[2];
            var mp2 = result.Values[3];
            var mp3 = result.Values[4];

            CheckMap(mx, new[] {Mapping(mp1, 42), Mapping(mp2, 2), Mapping(mp3, 3)});
            CheckMap(my, new[] {Mapping(mp1, 42), Mapping(mp3, 3)}, notExistKeys: new[] {mp2});
        }

        [Fact]
        public void TestMapRemoveRange()
        {
            var result = EvaluateSpec(@"
namespace M {
    export const p1 = 'a/path';
    export const p2 = 'b/path';
    export const p3 = 'c/path';
    export const x = Map.empty().addRange([p1, 1], [p2, 2], [p3, 3], [p1, 42]);
    export const y = x.removeRange(p2, p1);
}", new[] { "M.x", "M.y", "M.p1", "M.p2", "M.p3" });

            result.ExpectNoError();
            result.ExpectValues(5);

            var mx = result.Values[0] as DsMap;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsMap;
            Assert.NotNull(my);

            var mp1 = result.Values[2];
            var mp2 = result.Values[3];
            var mp3 = result.Values[4];

            CheckMap(mx, new[] { Mapping(mp1, 42), Mapping(mp2, 2), Mapping(mp3, 3) });
            CheckMap(my, new[] { Mapping(mp3, 3) }, notExistKeys: new[] { mp1, mp2 });
        }

        [Fact]
        public void TestMapToArray()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Map.empty().addRange([p1, 1], [p2, 2], [p3, 3], [p1, 42]);
    const xArray = x.toArray();
    export const y = Map.empty().addRange(...xArray);
}", new[] { "M.x", "M.y" });

            result.ExpectNoError();
            result.ExpectValues(2);

            var mx = result.Values[0] as DsMap;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsMap;
            Assert.NotNull(my);

            // To make ReSharper happy.
            Contract.Assume(my != null);

            CheckMap(mx, my.ToArray().Select(v => new KeyValuePair<object, object>(v.Key.Value, v.Value.Value)).ToArray());
        }

        [Fact]
        public void TestMapForEach()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Map.empty<string, number>().addRange([p1, 1], [p2, 2], [p3, 3], [p1, 42]);
    const w: any = x.forEach(kvp => { return [kvp[0], kvp[1] + 1]; });
    export const y: any = Map.empty().addRange(...w);
}", new[] { "M.x", "M.y" });

            result.ExpectNoError();
            result.ExpectValues(2);

            var mx = result.Values[0] as DsMap;
            Assert.NotNull(mx);

            var my = result.Values[1] as DsMap;
            Assert.NotNull(my);

            // To make ReSharper happy.
            Contract.Assume(my != null);
            Contract.Assume(mx != null);

            CheckMap(my, mx.ToArray().Select(kvp => Mapping(kvp.Key.Value, ((int) kvp.Value.Value) + 1)));
        }

        [Fact]
        public void TestMapKeys()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Map.empty().addRange([p1, 1], [p2, 2], [p3, 3], [p1, 42]);
    const w = x.keys();
    export const y = w.reduce((accum, val) => { return accum && x.containsKey(val); }, true);
}", new[] { "M.y" });

            result.ExpectNoError();
            result.ExpectValues(1);
            Assert.Equal(true, result.Values[0]);
        }

        [Fact]
        public void TestMapCount()
        {
            var result = EvaluateSpec(@"
namespace M {
    const p1 = 'a/path';
    const p2 = 'b/path';
    const p3 = 'c/path';
    export const x = Map.empty().addRange([p1, 1], [p2, 2], [p3, 3], [p1, 42]).count();
    
}", new[] { "M.x" });

            result.ExpectNoError();
            result.ExpectValues(1);
            Assert.Equal(3, result.Values[0]);
        }

        [Fact]
        public void TestMapOrdering()
        {
            var result = EvaluateSpec(@"
namespace M {
    const a = 'a/path';
    const b = 'b/path';
    const c = 'c/path';
    const d = 'd/path';

    const pa  = [a, 1];
    const pa1 = [a, 999];
    const pb  = [b, 2];
    const pc  = [c, 3];
    const pd  = [d, 4];

    const ps1 = [pa, pb, pc];
    
    const psDup: any = [...ps1, pa];
    const psAdd: any = [...ps1, pa1];
    
    const x = Map.empty().addRange(...psDup);
    const y = Map.empty().addRange(...psAdd);
    const z = x.remove(b);
    const w = x.add(pd[0], pd[1]).removeRange(a, c);
    const yKeys = y.keys();
    const yValues = y.values();


    export const checkX = checkKVP(x.toArray(), [pa, pb, pc]);
    export const checkY = checkKVP(y.toArray(), [pb, pc, pa1]);
    export const checkZ = checkKVP(z.toArray(), [pa, pc]);
    export const checkW = checkKVP(w.toArray(), [pb, pd]);
    export const checkYKeys = check(yKeys, [b, c, a]);
    export const checkYValues = check(yValues, [2, 3, 999]);

    function checkKVP(array1 : any[], array2 : any[]) {
        if (array1.length !== array2.length) return false;
        return array1.map((e, i) => e[0] === array2[i][0] && e[1] === array2[i][1]).all(b => b);
    }

    function check(array1 : any[], array2 : any[]) {
        if (array1.length !== array2.length) return false;
        return array1.map((e, i) => e === array2[i]).all(b => b);
    }
}", new[] { "M.checkX", "M.checkY", "M.checkZ", "M.checkW", "M.checkYKeys", "M.checkYValues" });

            result.ExpectNoError();
            result.ExpectValues(6);
            Assert.All(result.Values, e => Assert.Equal(true, e));
        }

        private static void CheckMap(DsMap map, IEnumerable<KeyValuePair<object, object>> keyValuePairs, bool exactMatch = true, IEnumerable<object> notExistKeys = null)
        {
            Contract.Requires(map != null);
            Contract.Requires(keyValuePairs != null);

            var kvps = keyValuePairs as KeyValuePair<object, object>[] ?? keyValuePairs.ToArray();

            foreach (var kvp in kvps)
            {
                var key = EvaluationResult.Create(kvp.Key);
                Assert.True(map.ContainsKey(key));

                Assert.Equal(kvp.Value, map.GetValue(key).Value);
            }

            if (exactMatch)
            {
                Assert.Equal(map.Count, kvps.Length);
            }

            if (notExistKeys != null)
            {
                foreach (var key in notExistKeys)
                {
                    Assert.False(map.ContainsKey(EvaluationResult.Create(key)));
                }
            }
        }

        private static KeyValuePair<object, object> Mapping(object key, object value)
        {
            return new KeyValuePair<object, object>(key, value);
        }
    }
}

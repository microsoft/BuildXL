// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

using BuildXL.Execution.Analyzer.JPath;
using BuildXL.FrontEnd.Script.Debugger;
using Newtonsoft.Json.Linq;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    public class JPathEvaluatorTests : XunitBuildXLTest
    {
#pragma warning disable CS0649 // unused field
        private class Val
        {
            public string S;
            public long N;
            public Val V;
            public long[] AN;
            public Val[] AV;
        }

        private class Env
        {
            public Val Curr;
            public Dictionary<string, object> Vars;
            public Env Parent;
        }
#pragma warning restore CS0649

        private Evaluator.Env RootEnv { get; }

        public JPathEvaluatorTests(ITestOutputHelper output)
           : base(output)
        {
            RootEnv = new Evaluator.Env(
                parent: null,
                current: Evaluator.Result.Scalar(new Val()),
                resolver: Resolver,
                vars: global::BuildXL.Execution.Analyzer.LibraryFunctions.All.ToDictionary(
                    func => "$" + func.Name, func => Evaluator.Result.Scalar(func)));
        }

        [Theory]
        // root expr 
        [InlineData("$.N", "{Curr: {N: 1}}",                         "[1]")]
        [InlineData("$.N", "{Curr: {N: 1}, Parent: {Curr: {N: 2}}}", "[2]")]
        [InlineData("$.X", "{Curr: {N: 1}, Parent: {Curr: {N: 2}}}", "[]")]
        // this expr 
        [InlineData("_.N", "{Curr: {N: 1}}",                         "[1]")]
        [InlineData("_.N", "{Curr: {N: 1}, Parent: {Curr: {N: 2}}}", "[1]")]
        [InlineData("_.X", "{Curr: {N: 1}, Parent: {Curr: {N: 2}}}", "[]")]
        // var expr
        [InlineData("$x", "{Vars: {'$x': 1}}",                            "[1]")]
        [InlineData("$y", "{Vars: {'$x': 1}}",                            "[]")]
        [InlineData("$y", "{Vars: {'$x': 1}, Parent: {Vars: {'$y': 2}}}", "[2]")]
        [InlineData("$y", "{Vars: {'$y': 1}, Parent: {Vars: {'$y': 2}}}", "[1]")]
        // union / arithmetic addition
        [InlineData("1 + 1",   "{}", "[2]")]
        [InlineData("1 @+ 1",  "{}", "[1]")]
        [InlineData("1 ++ 1",  "{}", "[1, 1]")]
        [InlineData("1 @+ 2",  "{}", "[1, 2]")]
        [InlineData("1 + 'a'", "{}", "[1, 'a']")]
        // difference / arithmetic subtraction
        [InlineData("1 - 1",  "{}", "[0]")]
        [InlineData("1 @- 1", "{}", "[]")]
        [InlineData("1 @- 2", "{}", "[1]")]
        // other set operators
        [InlineData("(1 ++ 2) & 1",        "{}", "[1]")]
        [InlineData("(1 ++ 2) & (1 ++ 3)", "{}", "[1]")]
        [InlineData("(1 ++ 2) & (3 ++ 5)", "{}", "[]")]
        // match
        [InlineData("'Hello World' ~ 'wor'",            "{}", "[true]")]
        [InlineData("'Hello World' ~ 'Wor'",            "{}", "[true]")]
        [InlineData("'Hello World' ~ /wor/",            "{}", "[false]")]
        [InlineData("'Hello World' ~ /(?i)wor/",        "{}", "[true]")]
        [InlineData("('Hello' ++ 'World') ~ /(?i)wor/", "{}", "[true]")]
        [InlineData("('Hello' ++ 'World') ~ /Hel/",     "{}", "[true]")]
        [InlineData("('Hello' ++ 'World') ~ /hel/",     "{}", "[false]")]
        // negative match 
        [InlineData("'Hello World' !~ 'wor'",     "{}", "[false]")]
        [InlineData("'Hello World' !~ 'Wor'",     "{}", "[false]")]
        [InlineData("'Hello World' !~ /wor/",     "{}", "[true]")]
        [InlineData("'Hello World' !~ /(?i)wor/", "{}", "[false]")]
        // index expression
        [InlineData("AN[0]",             "{Curr: {AN: [1, 2, 3]}}", "[1]")]
        [InlineData("(1 ++ 2 ++ 3)[-1]", "{}",                      "[3]")]
        [InlineData("(1 ++ 2 ++ 3)[-3]", "{}",                      "[1]")]
        [InlineData("(1 ++ 2 ++ 3)[5]",  "{}",                      "[]")]
        [InlineData("(1 ++ 2 ++ 3)[-5]", "{}",                      "[]")]
        [InlineData("(1 ++ 2 ++ 3)[1]",  "{}",                      "[2]")]
        // range expression
        [InlineData("AN[0..0]",              "{Curr: {AN: [1, 2, 3]}}", "[1]")]
        [InlineData("(1 ++ 2 ++ 3)[0..0]",   "{}",                      "[1]")]
        [InlineData("(1 ++ 2 ++ 3)[0..1]",   "{}",                      "[1, 2]")]
        [InlineData("(1 ++ 2 ++ 3)[0..-1]",  "{}",                      "[1, 2, 3]")]
        [InlineData("(1 ++ 2 ++ 3)[-2..-1]", "{}",                      "[2, 3]")]
        [InlineData("(1 ++ 2 ++ 3)[2..1]",   "{}",                      "[]")]
        [InlineData("(1 ++ 2 ++ 3)[-1..-2]", "{}",                      "[]")]
        // map expr
        [InlineData("AV.N",  "{Curr: {AV: [{N: 1}, {N: 2}, {N: 3}]}}",      "[1, 2, 3]")]
        [InlineData("AV.S",  "{Curr: {AV: [{S: '1'}, {}, {'S': '3'}]}}",    "['1', '3']")]
        [InlineData("AV.AN", "{Curr: {AV: [{AN: [1]}, {}, {AN: [2, 3]}]}}", "[1, 2, 3]")]
        // filter expr
        [InlineData("AV[N > 1].N",     "{Curr: {AV: [{N: 1}, {N: 2}, {N: 3}]}}",        "[2, 3]")]
        [InlineData("AV[X > 1].N",     "{Curr: {AV: [{N: 1}, {N: 2}, {N: 3}]}}",        "[]")]
        [InlineData("AV[S ~ '1'].S",   "{Curr: {AV: [{S: '1'}, {S: '2'}, {S: '12'}]}}", "['1', '12']")]
        [InlineData("AV[S ~ /^1$/].S", "{Curr: {AV: [{S: '1'}, {S: '2'}, {S: '12'}]}}", "['1']")]
        // cardinality 
        [InlineData("#(1 ++ 2 ++ 3)", "{}",                      "[3]")]
        [InlineData("#X",             "{Curr: {AN: [1, 2, 3]}}", "[0]")]
        // object literals
        [InlineData("{num: N, str: S}", "{Curr: {N: 1, S: 'a'}}", "[{num: 1, str: 'a'}]")]
        [InlineData("{N, S}",           "{Curr: {N: 1, S: 'a'}}", "[{Item0: 1, Item1: 'a'}]")]
        [InlineData("{a: 1, s: 's'}",   "{}",                     "[{a: 1, s: 's'}]")]
        // let binding
        [InlineData("let $a := 1 in $a + 2",                   "{}", "[3]")]
        [InlineData("let $a := 1 in (let $b := 2 in $a + $b)", "{}", "[3]")]
        public void TestEval(string exprStr, string envStr, string expectedResultJson)
        {
            var env = Convert(JsonDeserialize<Env>(envStr));
            var evaluator = new Evaluator(env, enableCaching: false, enableParallel: false);
            EvaluateAndAssertResult(evaluator, exprStr, expectedResultJson);
        }

        [Theory]
        // grep tests
        [InlineData("$grep('A', 'ab' ++ 'cd')",                    "['ab']")]
        [InlineData("$grep -o ('A', 'ab' ++ 'cd')",                "['a']")]
        [InlineData("$grep(/.$/, 'ab' ++ 'cd')",                   "['ab', 'cd']")]
        [InlineData("$grep -o (/.$/, 'ab' ++ 'cd')",               "['b', 'd']")]
        [InlineData("$grep -o -g 'G' (/(?<G>.).$/, 'ab' ++ 'cd')", "['a', 'c']")]
        // grep -v 
        [InlineData("$grep -v ('A', 'ab' ++ 'cd')",                   "['cd']")]
        [InlineData("$grep -v -o ('A', 'ab' ++ 'cd')",                "['cd']")]
        [InlineData("$grep -v (/.$/, 'ab' ++ 'cd')",                  "[]")]
        [InlineData("$grep -v -o (/.$/, 'ab' ++ 'cd')",               "[]")]
        [InlineData("$grep -v -o -g 'G' (/(?<G>.).$/, 'ab' ++ 'cd')", "[]")]
        // sort numeric
        [InlineData("(111 ++ 3 ++ 22) | $sort -n",    "[3, 22, 111]")]
        [InlineData("(111 ++ 3 ++ 22) | $sort -n -r", "[111, 22, 3]")]
        // sort as string
        [InlineData("(111 ++ 3 ++ 22) | $sort",       "[111, 22, 3]")]
        [InlineData("(111 ++ 3 ++ 22) | $sort -r",    "[3, 22, 111]")]
        // sort by field
        [InlineData("({a: 1, b: 2} ++ {a: 2, b: 1}) | $sort -n -k 'a'", "[{a: 1, b: 2}, {a: 2, b: 1}]")]
        [InlineData("({a: 1, b: 2} ++ {a: 2, b: 1}) | $sort -n -k 'b'", "[{a: 2, b: 1}, {a: 1, b: 2}]")]
        // uniq
        [InlineData("(1 ++ 2 ++ 1) | $uniq",    "[1, 2]")]
        [InlineData("(1 ++ 2 ++ 1) | $uniq -c", "[{Key: '1', Count: 2, Elems: [1, 1]}, {Key: '2', Count: 1, Elems: [2]}]")]
        // uniq by field
        [InlineData("({a: 1} ++ {a: 2} ++ {a: 3, b: 2}) | $uniq -k 'a' | $count", "[3]")]
        [InlineData("({a: 1} ++ {a: 2} ++ {a: 3, b: 2}) | $uniq -k 'b' | $count", "[2]")]
        [InlineData("({a: 1} ++ {a: 2} ++ {a: 3, b: 2}) | $uniq -k 'c' | $count", "[1]")]
        // uniq + sort
        [InlineData("(('a' ++ 'b' ++ 'a') | $uniq -c | $sort -n -r -k 'Count').($str(Count, ': ', Key))", "['2: a', '1: b']")]
        public void TestLibraryFunc(string exprStr, string expectedResultJson)
        {
            var evaluator = new Evaluator(RootEnv, enableCaching: false, enableParallel: false);
            EvaluateAndAssertResult(evaluator, exprStr, expectedResultJson);
        }

        private void EvaluateAndAssertResult(Evaluator evaluator, string exprStr, string expectedResultJson)
        {
            var maybeResult = JPath.TryParse(exprStr).Then(expr => JPath.TryEval(evaluator, expr));
            XAssert.IsTrue(maybeResult.Succeeded);
            var result = maybeResult.Result;

            var j1 = JArray.Parse(expectedResultJson);
            var j2 = JArray.Parse(JsonSerialize(result.ToArray()));
            XAssert.IsTrue(JToken.DeepEquals(j1, j2), "Expected: {0}, Actual: {1}", j1, j2);
        }

        private Evaluator.Env CreateEnv(object current, Evaluator.Env parent = null)
        {
            return new Evaluator.Env(parent, Resolver(current), Resolver);
        }

        private static ObjectInfo Resolver(object obj)
        {
            return obj switch
            {
                int i              => new ObjectInfo(preview: i.ToString(), original: i),
                string str         => new ObjectInfo(preview: str, original: str),
                ObjectInfo      oi => oi,
                _                  => Renderer.GenericObjectInfo(obj).Build()
            };
        }

        private static Property MakeProperty(KeyValuePair<string, string> kvp)
        {
            return new Property(name: kvp.Key, value: kvp.Value);
        }

        private static T JsonDeserialize<T>(string json)
        {
            return json == null ? default : Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
        }

        private static string JsonSerialize(object obj)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(convert(obj));

            object convert(object o)
            {
                return o switch
                {
                    ObjectInfo io      => io.Properties.ToDictionary(p => p.Name, p => convert(p.Value)),
                    object[] arr       => arr.Select(convert).ToArray(),
                    Evaluator.Result r => convert(r.IsScalar ? r.First() : r.ToArray()),
                    _                  => o
                };
            }
        }

        private Evaluator.Env Convert(Env env)
        {
            return env == null
                ? null
                : new Evaluator.Env(
                    parent: Convert(env.Parent),
                    current: Evaluator.Result.Scalar(env.Curr ?? new Val()),
                    resolver: Resolver,
                    vars: env.Vars?.ToDictionary(kvp => kvp.Key, kvp => Evaluator.Result.Scalar(kvp.Value)));
        }
    }
}

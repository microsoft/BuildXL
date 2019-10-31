// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BuildXL.Execution.Analyzer.JPath;
using BuildXL.FrontEnd.Script.Debugger;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    public class JPathEvaluatorTests : XunitBuildXLTest
    {
        private class Val
        {
            public string S;
            public string S2;
            public long N;
            public long N2;
            public object[] A;
            public object[] A2;
            public Val C;
            public Val C2;
        }

        private class Env
        {
            public Val Curr;
            public Dictionary<string, Val> Vars;
            public Env Parent;
        }

        public JPathEvaluatorTests(ITestOutputHelper output)
           : base(output)
        {
        }

        [Theory]
        // root expr tests
        [InlineData("$", "{N: 1}", null, new[] { "{N: 1}" })]
        [InlineData("$", "{N: 1}", "{N: 2}", new[] { "{N: 2}" })]
        public void TestEval(string exprStr, string envStr, object[] expectedResult)
        {
            var env = Convert(Deserialize<Env>(envStr));
            var evaluator = new Evaluator(topEnv, enableCaching: false);
            var maybeResult = JPath.TryParse(exprStr).Then(expr => JPath.TryEval(evaluator, expr));
            XAssert.IsTrue(maybeResult.Succeeded);

            XAssert.ArrayEqual(
                expectedResult.Select(JsonSerialize).ToArray(),
                maybeResult.Result.Value.Select(JsonSerialize).ToArray());
        }

        private object JsonSerialize(object obj)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(obj);
        }

        private Evaluator.Env CreateEnv(object current, Evaluator.Env parent = null)
        {
            return new Evaluator.Env(parent, Resolver(current), Resolver);
        }

        private ObjectInfo Resolver(object obj)
        {
            return obj switch
            {
                int i      => new ObjectInfo(preview: i.ToString(), original: i),
                string str => new ObjectInfo(preview: str, original: str),
                _ => Renderer.GenericObjectInfo(obj)
            };
        }

        private static Property MakeProperty(KeyValuePair<string, string> kvp)
        {
            return new Property(name: kvp.Key, value: kvp.Value);
        }

        private static T Deserialize<T>(string json)
        {
            return json == null ? default : Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
        }
    }
}

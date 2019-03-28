// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator.Profiling;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using Test.DScript.Ast.DScriptV2;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Profiler
{
    public class ProfilerTests : SemanticBasedTests
    {
        private readonly ProfilerDecorator m_profilerDecorator;

        public ProfilerTests(ITestOutputHelper output)
            : base(output)
        {
            m_profilerDecorator = new ProfilerDecorator();
        }

        /// <summary>
        /// All these tests use the profiler decorator
        /// </summary>
        protected override IDecorator<EvaluationResult> DecoratorForEvaluation => m_profilerDecorator;

        [Fact]
        public void TestProfilerAmbientCall()
        {
            string code = @"
export const x= Debug.writeLine('Test');
";
            BuildLegacyConfigurationWithPrelude()
                .AddSpec("package.dsc", code)
                .RootSpec("package.dsc")
                .EvaluateWithNoErrors();

            var entries = m_profilerDecorator.GetProfiledEntries();
            Assert.Equal(1, entries.Count);

            var entry = entries.First();

            Assert.Equal(@"Debug.writeLine(""Test"")", entry.CallsiteInvocation);
            Assert.Equal("<ambient call>", entry.FunctionLocation);
        }

        [Fact]
        public void TestProfilerPropertyCall()
        {
            string code = @"
export const x = ""a"".toString();
";
            BuildLegacyConfigurationWithPrelude()
                .AddSpec("package.dsc", code)
                .RootSpec("package.dsc")
                .EvaluateWithNoErrors();

            var entries = m_profilerDecorator.GetProfiledEntries();
            Assert.Equal(1, entries.Count);

            var entry = entries.First();

            Assert.Equal(@"""a"".toString()", entry.CallsiteInvocation);
            Assert.Equal("<ambient call>", entry.FunctionLocation);
        }

        [Fact]
        public void TestProfilerRegularCall()
        {
            string code =
@"export const x = test('hi');

function test(s: string) {
}
";
            BuildLegacyConfigurationWithPrelude()
                .AddSpec("package.dsc", code)
                .RootSpec("package.dsc")
                .EvaluateWithNoErrors();

            var entries = m_profilerDecorator.GetProfiledEntries();
            Assert.Equal(1, entries.Count);

            var entry = entries.First();

            Assert.Equal(@"test(""hi"")", entry.CallsiteInvocation);
            Assert.Equal(1, entry.CallsiteLocation.Line);
            Assert.Equal("test", entry.FunctionName);
        }
    }
}

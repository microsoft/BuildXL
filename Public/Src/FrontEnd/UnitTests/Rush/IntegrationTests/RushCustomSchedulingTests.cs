// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;
using LogEventId = global::BuildXL.FrontEnd.Core.Tracing.LogEventId;

namespace Test.BuildXL.FrontEnd.Rush.IntegrationTests
{
    [Trait("Category", "RushCustomSchedulingTests")]
    public class RushCustomSchedulingTests : RushIntegrationTestBase
    {
        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        public RushCustomSchedulingTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        // The callback is not a public exported value
        [InlineData("namespace Test { function custom(project: JavaScriptProject) : TransformerExecuteResult { return undefined;}}")]
        // The callback does not exist
        [InlineData("namespace NotThere {}")]
        // The callback has the wrong type
        [InlineData("namespace Test { @@public export function custom(project: JavaScriptProject) : boolean { return true;}}")]
        public void CallbackIsValidated(string callbackDefinition)
        {
            var config =
                Build(
                    schedulingCallback: "{module: 'myModule', schedulingFunction: 'Test.custom'}",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .AddSpec("module.config.dsc", "module({name: 'myModule'});")
               .AddSpec(callbackDefinition)
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(LogEventId.CheckerError);
            AssertErrorEventLogged(LogEventId.CannotBuildWorkspace);
        }

        [Theory]
        // The provided argument is a boolean but should be a string
        [InlineData("<LazyEval<any>>{expression: 'true'}", LogEventId.CheckerError)]
        // The provided argument is a parsing error
        [InlineData("<LazyEval<any>>{expression: 'invalid expression'}", LogEventId.TypeScriptSyntaxError)]
        public void CallbackArgumentIsValidated(string argumentExpression, LogEventId expectedErrorId)
        {
            var config =
                Build(
                    schedulingCallback: $"{{module: 'myModule', schedulingFunction: 'Test.custom', argument: {argumentExpression}}}",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .AddSpec("module.config.dsc", "module({name: 'myModule'});")
               .AddSpec("@@public export function custom(project: JavaScriptProject, argument: string) : TransformerExecuteResult { return undefined; }")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(expectedErrorId);
            AssertErrorEventLogged(LogEventId.CannotBuildWorkspace);
        }

        [Fact]
        public void CallbackEvaluationErrorIsHandled()
        {
            var config =
                Build(
                    schedulingCallback: "{module: 'myModule', schedulingFunction: 'custom'}",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .AddSpec("module.config.dsc", "module({name: 'myModule'});")
               .AddSpec(@"
@@public
export function custom(project: JavaScriptProject) : TransformerExecuteResult {
    Contract.fail('Fail!');
    return undefined;
}
")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.ProjectGraphConstructionError);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Script.Tracing.LogEventId.ContractFail);
        }

        [Fact]
        public void JavaScriptProjectArgumentIsWellFormed()
        {
            var config =
                Build(
                    schedulingCallback: "{module: 'myModule', schedulingFunction: 'custom'}",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "run") })
               .AddSpec("module.config.dsc", "module({name: 'myModule'});")
               .AddSpec(@"
@@public
export function custom(project: JavaScriptProject) : TransformerExecuteResult {

    Debug.writeLine(project);
    Contract.requires(project.name === '@ms/project-A');
    Contract.requires(project.scriptCommandName === 'build');
    Contract.requires(project.scriptCommand === 'run');
    Contract.requires(project.projectFolder === d`src\A`);
    Contract.requires(project.inputs.some(input => input === f`src\A\package.json`));
    Contract.requires(project.outputs.some(output => output === d`src\A`));
    Contract.requires(project.environmentVariables.some(envVar => envVar.name === 'PATH'));
    Contract.requires(project.passThroughEnvironmentVariables.length === 0);
    Contract.requires(project.tempDirectory.name === a`t`);

    return undefined;
}
")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(result.IsSuccess);
        }

        [Theory]
        [InlineData("string", "'testValue'", "'testValue'")]
        [InlineData("number", "1+2", "3")]
        [InlineData("boolean", "true", "true")]
        public void CallbackArgumentIsEvaluated(string type, string initializer, string expectedValue)
        {
            var config =
                Build(
                    schedulingCallback: "{module: 'myModule', schedulingFunction: 'custom', argument: <LazyEval<any>>{expression: 'importFrom(\\\"myModule\\\").variableName'} }",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .AddSpec("module.config.dsc", "module({name: 'myModule'});")
               .AddSpec("schedulingFunction.dsc", $@"
@@public
export function custom(project: JavaScriptProject, argument: {type}) : TransformerExecuteResult {{

    Debug.writeLine(argument);
    Contract.requires(argument === {expectedValue});

    return undefined;
}}
")
               .AddSpec("schedulingFunctionArgument.dsc", $@"
@@public export
const variableName : {type} = {initializer};
")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void CallbackArgumentIsUndefinedWhenItIsNotSpecified()
        {
            var config =
                Build(
                    schedulingCallback: "{module: 'myModule', schedulingFunction: 'custom' }",
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A")
               .AddSpec("module.config.dsc", "module({name: 'myModule'});")
               .AddSpec("schedulingFunction.dsc", @"
@@public
export function custom(project: JavaScriptProject, argument: string) : TransformerExecuteResult {

    Debug.writeLine(argument);
    Contract.requires(argument === undefined);

    return undefined;
}
")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void JavaScriptProjectEnvFiltering()
        {
            Dictionary<string, DiscriminatingUnion<string, UnitValue>> envVars = new Dictionary<string, DiscriminatingUnion<string, UnitValue>>();
            envVars.Add("TMP", new DiscriminatingUnion<string, UnitValue>("/SamplePath"));      // Should be filtered
            envVars.Add("TEMP", new DiscriminatingUnion<string, UnitValue>("/SamplePath"));     // Should be filtered
            envVars.Add("NOTTEMP", new DiscriminatingUnion<string, UnitValue>("/SamplePath"));  // Should not be filtered
            envVars.Add("PASSTHROUGH", new DiscriminatingUnion<string, UnitValue>(UnitValue.Unit));  // Should not be filtered


            var config =
                Build(
                    schedulingCallback: "{module: 'myModule', schedulingFunction: 'custom'}",
                    environment: envVars,
                    addDScriptResolver: true)
               .AddJavaScriptProject("@ms/project-A", "src/A", scriptCommands: new[] { ("build", "run") })
               .AddSpec("module.config.dsc", "module({name: 'myModule'});")
               .AddSpec(@"
@@public
export function custom(project: JavaScriptProject) : TransformerExecuteResult {

    Debug.writeLine(project);
    Contract.requires(project.environmentVariables.some(envVar => envVar.name === 'PATH'));
    Contract.requires(project.environmentVariables.some(envVar => envVar.name === 'NOTTEMP'));
    Contract.requires(!project.environmentVariables.some(envVar => envVar.name === 'TEMP'));
    Contract.requires(!project.environmentVariables.some(envVar => envVar.name === 'TMP'));
    Contract.requires(project.tempDirectory.name === a`t`);

    Contract.requires(project.passThroughEnvironmentVariables.some(envVar => envVar === 'PASSTHROUGH'));

    return undefined;
}
")
               .PersistSpecsAndGetConfiguration();

            var result = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
            });

            Assert.True(result.IsSuccess);
        }
    }
}

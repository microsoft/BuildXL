// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    }
}

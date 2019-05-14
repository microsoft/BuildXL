// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.MsBuild.Tracing;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.FrontEnd.MsBuild.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Validates how targets are called based on the availability of target predictions
    /// </summary>
    public sealed class MsBuildTargetsTests : MsBuildPipSchedulingTestBase
    {
        public MsBuildTargetsTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.FrontEnd.MsBuild.ETWLogger.Log);
        }

        [Fact]
        public void LeafProjectNotImplementingTargetProtocolIsSuccesfullyScheduled()
        {
            var noTargetProtocolProject = CreateProjectWithPredictions(predictedTargetsToExecute: PredictedTargetsToExecute.Create(new string[] { "Build" }), implementsTargetProtocol: false);
            var process =
                Start()
                    .Add(noTargetProtocolProject)
                    .ScheduleAll()
                    .AssertSuccess();

            // The project doesn't have any references, so there should be an informational log
            AssertVerboseEventLogged(LogEventId.LeafProjectIsNotSpecifyingTheProjectReferenceProtocol, 1);
            AssertWarningCount();
        }

        [Fact]
        public void NonLeafProjectNotImplementingTargetProtocolIsSuccesfullBasedOnConfig()
        {
            var leafProject = CreateProjectWithPredictions(predictedTargetsToExecute: PredictedTargetsToExecute.Create(new[] { "Build" }));
            var nonLeafProject = CreateProjectWithPredictions(implementsTargetProtocol: false, references: new[] { leafProject });

            Start(new MsBuildResolverSettings { AllowProjectsToNotSpecifyTargetProtocol = true })
                    .Add(leafProject)
                    .Add(nonLeafProject)
                    .ScheduleAll()
                    .AssertSuccess();
            
            // if not specifying the protocol is allowed, we should succeed but log a warning
            AssertWarningEventLogged(LogEventId.ProjectIsNotSpecifyingTheProjectReferenceProtocol);
            AssertWarningCount();
        }

        [Fact]
        public void ProjectWithPredictedTargetsAreHonored()
        {
            var targetPredictedProject = CreateProjectWithPredictions(
                predictedTargetsToExecute: PredictedTargetsToExecute.Create(new[] {"foo"}));
            var process =
                Start()
                    .Add(targetPredictedProject)
                    .ScheduleAll()
                    .AssertSuccess()
                    .RetrieveSuccessfulProcess(targetPredictedProject);

            var arguments = RetrieveProcessArguments(process);

            Assert.Contains("/t:foo", arguments);
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.MsBuild.Tracing;
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
        public void ProjectWithNoPredictedTargetsGetScheduledWithDefaultTargets()
        {
            var noPredictionProject = CreateProjectWithPredictions(predictedTargetsToExecute: PredictedTargetsToExecute.PredictionNotAvailable);
            var process =
                Start()
                    .Add(noPredictionProject)
                    .ScheduleAll()
                    .AssertSuccess()
                    .RetrieveSuccessfulProcess(noPredictionProject);

            var arguments = RetrieveProcessArguments(process);

            // No targets should be explicitly passed (so MSBuild will pick defaults)
            Assert.DoesNotContain("/t", arguments);
            // The project doesn't have any references, so there should be an informational log
            AssertInformationalEventLogged(LogEventId.LeafProjectIsNotSpecifyingTheProjectReferenceProtocol, 1);
            AssertWarningEventLogged(LogEventId.ProjectIsNotSpecifyingTheProjectReferenceProtocol, 0);
            AssertWarningCount();
        }

        [Fact]
        public void ProjectWithNoPredictedTargetsAndReferencesGetScheduledWithDefaultTargetsAndWarn()
        {
            var referencedProject = CreateProjectWithPredictions(predictedTargetsToExecute: PredictedTargetsToExecute.PredictionNotAvailable);
            var noPredictionProject = CreateProjectWithPredictions(predictedTargetsToExecute: PredictedTargetsToExecute.PredictionNotAvailable, references: new [] { referencedProject });
            var process =
                Start()
                    .Add(referencedProject)
                    .Add(noPredictionProject)
                    .ScheduleAll()
                    .AssertSuccess()
                    .RetrieveSuccessfulProcess(noPredictionProject);

            var arguments = RetrieveProcessArguments(process);

            // No targets should be explicitly passed (so MSBuild will pick defaults)
            Assert.DoesNotContain("/t", arguments);
            // We should warn when this is the case
            AssertWarningEventLogged(LogEventId.ProjectIsNotSpecifyingTheProjectReferenceProtocol);
        }

        [Fact]
        public void ProjectWithPredictedTargetsAreHonored()
        {
            var targetPredictedProject = CreateProjectWithPredictions(
                predictedTargetsToExecute: PredictedTargetsToExecute.CreatePredictedTargetsToExecute(new[] {"foo"}));
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

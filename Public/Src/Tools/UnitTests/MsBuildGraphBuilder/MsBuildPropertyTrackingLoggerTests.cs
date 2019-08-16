using System;
using System.Linq;
using Microsoft.Build.Framework;
using ProjectGraphBuilder;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.ProjectGraphBuilder
{
    public class MsBuildPropertyTrackingLoggerTests : XunitBuildXLTest
    {
        public MsBuildPropertyTrackingLoggerTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void TracksEnvironmentVariableReads()
        {
            var eventSource = new TestEventSource();
            var logger = new PropertyTrackingLogger();
            logger.Initialize(eventSource);

            string envVarName = Guid.NewGuid().ToString();
            string message = "unused";
            eventSource.FireAnyEvent(new EnvironmentVariableReadEventArgs(envVarName, message));

            Assert.NotNull(logger.PotentialEnvironmentVariableReads);
            Assert.True(logger.PotentialEnvironmentVariableReads.Succeeded);
            Assert.NotNull(logger.PotentialEnvironmentVariableReads.Result);
            Assert.True(logger.PotentialEnvironmentVariableReads.Result.Contains(envVarName));
            Assert.Equal(1, logger.PotentialEnvironmentVariableReads.Result.Count);
        }

        [Fact]
        public void TracksUninitializedPropertyReads()
        {
            var eventSource = new TestEventSource();
            var logger = new PropertyTrackingLogger();
            logger.Initialize(eventSource);

            string propertyName = Guid.NewGuid().ToString();
            string message = "unused";
            eventSource.FireAnyEvent(new UninitializedPropertyReadEventArgs(propertyName, message));

            Assert.NotNull(logger.PotentialEnvironmentVariableReads);
            Assert.True(logger.PotentialEnvironmentVariableReads.Succeeded);
            Assert.NotNull(logger.PotentialEnvironmentVariableReads.Result);
            Assert.True(logger.PotentialEnvironmentVariableReads.Result.Contains(propertyName));
            Assert.Equal(1, logger.PotentialEnvironmentVariableReads.Result.Count);
        }

        private class TestEventSource : IEventSource
        {
            public void FireAnyEvent(BuildEventArgs e)
            {
                this.AnyEventRaised?.Invoke(this, e);
            }

            public event BuildMessageEventHandler MessageRaised;

            public event BuildErrorEventHandler ErrorRaised;

            public event BuildWarningEventHandler WarningRaised;

            public event BuildStartedEventHandler BuildStarted;

            public event BuildFinishedEventHandler BuildFinished;

            public event ProjectStartedEventHandler ProjectStarted;

            public event ProjectFinishedEventHandler ProjectFinished;

            public event TargetStartedEventHandler TargetStarted;

            public event TargetFinishedEventHandler TargetFinished;

            public event TaskStartedEventHandler TaskStarted;

            public event TaskFinishedEventHandler TaskFinished;

            public event CustomBuildEventHandler CustomEventRaised;

            public event BuildStatusEventHandler StatusEventRaised;

            public event AnyEventHandler AnyEventRaised;
        }
    }
}

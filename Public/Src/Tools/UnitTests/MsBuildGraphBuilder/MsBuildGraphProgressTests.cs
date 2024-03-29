// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using BuildXL.FrontEnd.MsBuild.Serialization;
using MsBuildGraphBuilderTool;
using Test.BuildXL.TestUtilities.Xunit;
using Test.ProjectGraphBuilder.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Test.ProjectGraphBuilder
{
    public class MsBuildGraphProgressTests : GraphBuilderToolTestBase
    {
        private readonly string m_entryPoint;

        public MsBuildGraphProgressTests(ITestOutputHelper output): base(output)
        {
            var builder = new MsBuildProjectBuilder(TestOutputDirectory);
            m_entryPoint = builder.WriteProjectsWithReferences("[SimpleProject]");
        }

        [Fact]
        public void GraphConstructionSucceedsEvenWhenNoClientListensToProgress()
        {
            using (var reporter = new GraphBuilderReporter(Guid.NewGuid().ToString()))
            {
                var result = BuildAndReport(reporter, out var failure);
                XAssert.IsTrue(result, failure);
            }
        }

        [Fact]
        public void NoClientListeningResultsInAReportedError()
        {
            GraphBuilderReporter reporter = null;
            try
            {
                reporter = new GraphBuilderReporter(Guid.NewGuid().ToString());
                BuildAndReport(reporter, out _);
            }
            finally
            {
                reporter.Dispose();
                XAssert.IsTrue(reporter.Errors.Count > 0);
            }
        }

        [Fact]
        public void ProgressIsReported()
        {
            var pipeName = Guid.NewGuid().ToString();
            var task = ConnectToServerPipeAndLogProgress(pipeName);
            using (var reporter = new GraphBuilderReporter(pipeName))
            {
                var result = BuildAndReport(reporter, out var failure);
                XAssert.IsTrue(result, failure);
            }

            var progress = task.GetAwaiter().GetResult();
            XAssert.IsTrue(!string.IsNullOrEmpty(progress));
        }

        private bool BuildAndReport(GraphBuilderReporter reporter, out string failure)
        {
            string outputFile = Path.Combine(TemporaryDirectory, Guid.NewGuid().ToString());
            var arguments = GetStandardBuilderArguments(
                new[] { m_entryPoint },
                outputFile,
                globalProperties: GlobalProperties.Empty,
                entryPointTargets: new string[0],
                requestedQualifiers: new GlobalProperties[] { GlobalProperties.Empty },
                allowProjectsWithoutTargetProtocol: false);

            MsBuildGraphBuilder.BuildGraphAndSerializeForTesting(AssemblyLoader, reporter, arguments);
            var result = SimpleDeserializer.Instance.DeserializeGraph(outputFile);

            failure = string.Empty;
            if (!result.Succeeded)
            {
                failure = result.Failure.Message;
            }

            return result.Succeeded;
        }

        private static Task<string> ConnectToServerPipeAndLogProgress(string pipeName)
        {
            return Task.Factory.StartNew(
                    () =>
                    {
                        var stringBuilder = new StringBuilder();
                        using (var pipeClient = new NamedPipeClientStream(
                            ".",
                            Path.GetFileName(pipeName),
                            PipeDirection.In,
                            PipeOptions.Asynchronous))
                        using (var reader = new StreamReader(pipeClient, Encoding.UTF8))
                        {
                            pipeClient.Connect(100000);
                            while (!reader.EndOfStream)
                            {
                                var line = reader.ReadLine();
                                if (line != null)
                                {
                                    stringBuilder.Append(line);
                                }
                            }
                        }
                        return stringBuilder.ToString();
                    }
                );
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.FrontEnd.Ninja.Infrastructure;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Ninja.SchedulingTests
{
    public class NinjaSchedulingTest : NinjaPipSchedulingTestBase
    {
        public NinjaSchedulingTest(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void CorrectScheduleGivenDependencyOrder()
        {
            var nodeA = CreateNinjaNode(rule: "ruleA", command: @"cmd /C ""echo hola > hola.txt""");
            var nodeB = CreateNinjaNode(rule: "ruleB", dependencies: new[] { nodeA });
            var nodeC = CreateNinjaNode(rule: "ruleC", dependencies: new[] { nodeA });
            var nodeD = CreateNinjaNode(rule: "ruleD", dependencies: new[] { nodeC, nodeB });

           Start().AddAll(nodeA, nodeB, nodeC, nodeD).ScheduleAll().AssertSuccess();
        }

        [Fact]
        public void CorrectInputs()
        {
            var node = CreateNinjaNode("myRule", inputs: Paths("input.o"));
            var inputs = Start().Add(node).ScheduleAll().RetrieveSuccessfulProcess(node).Dependencies;
            XAssert.IsTrue(inputs.Any(file => GetFileName(file) == "input.o"));
        }



        [Fact]
        public void DoNotSchedulePhonyProcess()
        {
            var trueNode = CreateNinjaNode(outputs: Paths("foo.out"));
            var phonyNode = CreateNinjaNode(rule: "phony", command: "", inputs: Paths("foo.out"));

            var process = Start().AddAll(trueNode, phonyNode).ScheduleAll().RetrieveSuccessfulProcess(phonyNode);
            XAssert.IsNull(process);
        }

        [Fact]
        public void CorrectSeparationOfQuotedExecutable()
        {
            var quotedBogus = $@"""{BogusExecutable}""";
            var options = "--great --options";
            var commandLine = $"{quotedBogus} {options}";

            var node = CreateNinjaNode(command: commandLine, outputs: Paths("foo.out"));
            var process = Start().Add(node).ScheduleAll().RetrieveSuccessfulProcess(node);
            XAssert.AreEqual(BogusExecutable, process.Executable.Path.ToString(PathTable));
            XAssert.AreEqual(options, process.Arguments.ToString(PathTable));
        }

        [Fact]
        public void CorrectSeparationOfExecutable()
        {
            var options = "--great --options";
            var commandLine = $"{BogusExecutable} {options}";

            var node = CreateNinjaNode(command: commandLine, outputs: Paths("foo.out"));
            var process = Start().Add(node).ScheduleAll().RetrieveSuccessfulProcess(node);
            XAssert.AreEqual(BogusExecutable, process.Executable.Path.ToString(PathTable));
            XAssert.AreEqual(options, process.Arguments.ToString(PathTable));
        }


        [Theory]
        [InlineData("/SomeArg /ZI", "/SomeArg /Z7 ")]
        [InlineData("/SomeArg /Zi", "/SomeArg /Z7 ")]
        [InlineData("/SomeArg /Zi /AnotherArg", "/SomeArg /Z7 /AnotherArg")]
        [InlineData("/SomeArg /Zi /FS /MP9 /AnotherArg", "/SomeArg /Z7 /AnotherArg")]
        [InlineData("/SomeArg /Zi /FS /AnotherArg /FS /MP9", "/SomeArg /Z7 /AnotherArg ")]
        [InlineData("/SomeArg /DEBUG /AnotherArg", "/SomeArg /DEBUG /AnotherArg")]
        public void ReplacePdbOptions(string rawArguments, string processedArguments)
        {
            var commandLine = $"{BogusExecutable} {rawArguments}";

            var node = CreateNinjaNode(command: commandLine, outputs: Paths("foo.out"));
            var process = Start().Add(node).ScheduleAll().RetrieveSuccessfulProcess(node);
            XAssert.AreEqual(processedArguments, process.Arguments.ToString(PathTable));
        }

        [Theory]
        [InlineData("/SomeArg /ZI", "/SomeArg ")]
        [InlineData("/SomeArg /Zi", "/SomeArg ")]
        [InlineData("/SomeArg /Zi /AnotherArg", "/SomeArg /AnotherArg")]
        [InlineData("/SomeArg /Zi /FS /MP8 /FS /AnotherArg", "/SomeArg /AnotherArg")]
        [InlineData("/SomeArg /DEBUG /AnotherArg", "/SomeArg /AnotherArg")]
        public void SuppressPdbOptionsWhenSettingIsTrue(string rawArguments, string processedArguments)
        {
            var commandLine = $"{BogusExecutable} {rawArguments}";

            var node = CreateNinjaNode(command: commandLine, outputs: Paths("foo.out"));

            // We want to remove all debug flags
            var settings = new NinjaResolverSettings();
            settings.RemoveAllDebugFlags = true;

            var process = Start(settings).Add(node).ScheduleAll().RetrieveSuccessfulProcess(node);
            XAssert.AreEqual(processedArguments, process.Arguments.ToString(PathTable));
        }

        [Fact]
        public void SemistableHashesArePreservedForTheSameSchedule()
        {
            var nodeA = CreateNinjaNode("ruleA", outputs: Paths("fileA.out"), inputs: Paths("input.txt"));
            var nodeB = CreateNinjaNode("ruleB", outputs: Paths("fileB.out"), inputs: Paths("inputB.txt"));

            var processes = Start()
                .Add(nodeA)
                .Add(nodeB)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveAllProcesses();

            var hashes = new HashSet<string>(processes.Select(pip => pip.FormattedSemiStableHash));

            // Schedule again with the same set of specs
            processes = Start()
                .Add(nodeA)
                .Add(nodeB)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveAllProcesses();

            var hashes2 = new HashSet<string>(processes.Select(pip => pip.FormattedSemiStableHash));

            // Semistable hashes of both runs must be equivalent
            Assert.True(HashSet<string>.CreateSetComparer().Equals(hashes, hashes2));
        }


        [Fact]
        public void MspdvsrvEndpointTest()
        {
            var nodeA = CreateNinjaNode("ruleA", command: "cmd /C echo first", outputs: Paths("fileA.out"), inputs: Paths("input.txt"));
            var nodeB = CreateNinjaNode("ruleB", command: "cmd /C echo second", outputs: Paths("fileB.out"), inputs: Paths("inputB.txt"));

            var processes = Start()
                .Add(nodeA)
                .Add(nodeB)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveAllProcesses();

            // Get the _MSPDBSRV_ENDPOINT_ set for each pip
            var endpoints = processes.Select(
                pip => pip.EnvironmentVariables.First(env => 
                            (env.Name.ToString(PathTable.StringTable) == "_MSPDBSRV_ENDPOINT_") && !string.IsNullOrWhiteSpace(env.Value.ToString(PathTable))))
                      .ToList();

            // Both should have the environment variable
            Assert.Equal(2, endpoints.Count());

            // They should have different values
            Assert.NotEqual(endpoints[0], endpoints[1]);


            // Schedule again the same nodes, the endpoints should be the same
            processes = Start()
                .Add(nodeA)
                .Add(nodeB)
                .ScheduleAll()
                .AssertSuccess()
                .RetrieveAllProcesses();

            var endpoints2 = processes.Select(
                    pip => pip.EnvironmentVariables.First(env =>
                                                              (env.Name.ToString(PathTable.StringTable) == "_MSPDBSRV_ENDPOINT_") && !string.IsNullOrWhiteSpace(env.Value.ToString(PathTable))))
                .ToList();

            Assert.True(endpoints2.Contains(endpoints[0]));
            Assert.True(endpoints2.Contains(endpoints[1]));

        }


        private string GetFileName(FileArtifact file) => file.Path.GetName(PathTable).ToString(PathTable.StringTable);


        private IReadOnlySet<AbsolutePath> Paths(params string[] paths)
        {
            return paths.Select(p => TestPath.Combine(PathTable, p)).ToReadOnlySet();
        }

    }
}

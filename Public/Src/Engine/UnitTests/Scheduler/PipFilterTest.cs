// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Ipc.Common;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Filter;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using Test.BuildXL.Scheduler.Utils;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.Scheduler
{
    [Feature(Features.Filtering)]
    public sealed class PipFilterTests : PipTestBase
    {
        public PipFilterTests(ITestOutputHelper output)
            : base(output)
        { }

        [Fact]
        public void TestTagFilter()
        {
            FileArtifact output = CreateOutputFileArtifact();
            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output},
                tags: new[] {"T1", "T2", "T3", "T4"});

            XAssert.IsTrue(IsFilterMatch(CreateTagFilter("T2"), p1));
            XAssert.IsFalse(IsFilterMatch(CreateTagFilter("asdf"), p1));
            XAssert.IsTrue(IsFilterMatch(CreateTagFilter("asdf").Negate(), p1));
        }

        [Fact]
        public void TestBinaryFilter()
        {
            FileArtifact output = CreateOutputFileArtifact();
            FileArtifact output2 = CreateOutputFileArtifact();
            FileArtifact notOutput = CreateOutputFileArtifact();
            AbsolutePath outputDir = CreateUniqueObjPath("D");
            AbsolutePath notOutputDir = CreateUniqueObjPath("ND");

            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output, output2},
                outputDirectoryPaths: new[] {outputDir},
                tags: new[] {"T1", "T2", "T3", "T4"});

            // These cover the cases where the left/right match no/all files
            XAssert.IsTrue(IsFilterMatch(new BinaryFilter(CreateTagFilter("T1"), FilterOperator.Or, CreateTagFilter("asdf")), p1));
            XAssert.IsTrue(IsFilterMatch(new BinaryFilter(CreateTagFilter("asdf"), FilterOperator.Or, CreateTagFilter("T1")), p1));
            XAssert.IsTrue(IsFilterMatch(new BinaryFilter(CreateTagFilter("T2"), FilterOperator.Or, CreateTagFilter("T1")), p1));
            XAssert.IsTrue(IsFilterMatch(new BinaryFilter(CreateTagFilter("T2"), FilterOperator.And, CreateTagFilter("T1")), p1));
            XAssert.IsFalse(IsFilterMatch(new BinaryFilter(CreateTagFilter("asdf"), FilterOperator.And, CreateTagFilter("T1")), p1));
            XAssert.IsFalse(IsFilterMatch(new BinaryFilter(CreateTagFilter("T2"), FilterOperator.And, CreateTagFilter("asdf")), p1));

            // Cover the cases where left/right match non-empty file sets
            XAssert.IsTrue(IsFilterMatch(new BinaryFilter(
                CreateOutputPathFilter(output.Path),
                FilterOperator.Or,
                CreateOutputPathFilter(notOutput.Path)
                ), p1));

            XAssert.IsTrue(IsFilterMatch(new BinaryFilter(
                CreateOutputPathFilter(notOutput.Path),
                FilterOperator.Or,
                CreateOutputPathFilter(output.Path)
                ), p1));

            XAssert.IsFalse(IsFilterMatch(new BinaryFilter(
                CreateOutputPathFilter(output.Path),
                FilterOperator.And,
                CreateOutputPathFilter(notOutput.Path)
                ), p1));

            XAssert.IsFalse(IsFilterMatch(new BinaryFilter(
                CreateOutputPathFilter(notOutput.Path),
                FilterOperator.And,
                CreateOutputPathFilter(output.Path)
                ), p1));

            // Cover cases for directories.
            XAssert.IsTrue(IsFilterMatch(new BinaryFilter(
                CreateOutputPathFilter(outputDir),
                FilterOperator.Or,
                CreateOutputPathFilter(notOutputDir)
                ), p1));

            XAssert.IsTrue(IsFilterMatch(new BinaryFilter(
                CreateOutputPathFilter(notOutputDir),
                FilterOperator.Or,
                CreateOutputPathFilter(outputDir)
                ), p1));

            XAssert.IsFalse(IsFilterMatch(new BinaryFilter(
                CreateOutputPathFilter(outputDir),
                FilterOperator.And,
                CreateOutputPathFilter(notOutputDir)
                ), p1));

            XAssert.IsFalse(IsFilterMatch(new BinaryFilter(
                CreateOutputPathFilter(notOutputDir),
                FilterOperator.And,
                CreateOutputPathFilter(outputDir)
                ), p1));
        }

        [Fact]
        public void TestBinaryFilterNegation()
        {
            FileArtifact output = CreateOutputFileArtifact();
            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output},
                tags: new[] {"T1", "T2", "T3", "T4"});

            XAssert.IsTrue(IsFilterMatch(
                new BinaryFilter(CreateTagFilter("T5"), FilterOperator.Or, CreateTagFilter("T6")).Negate(), p1));
        }

        [Fact]
        public void TestOutputFilter()
        {
            FileArtifact output = CreateOutputFileArtifact();
            FileArtifact notOutput = CreateOutputFileArtifact();
            AbsolutePath outputDir = CreateUniqueObjPath("D");
            AbsolutePath notOutputDir = CreateUniqueObjPath("ND");
            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output, CreateOutputFileArtifact()},
                outputDirectoryPaths: new[] {outputDir},
                tags: new[] {"T1", "T2", "T3", "T4"});

            AssertOutputFilterMatches(p1, output, notOutput);
            XAssert.IsTrue(IsFilterMatch(CreateOutputPathFilter(outputDir), p1));
            XAssert.IsFalse(IsFilterMatch(CreateOutputPathFilter(notOutputDir), p1));
        }

        [Feature(Features.IpcPip)]
        [Fact]
        public void TestOutputFilterWithIpcPip()
        {
            IpcPip p2 = IpcPip.CreateFromStringPayload(
                Context,
                ObjectRootPath,
                new IpcClientInfo(new DummyIpcProvider().CreateNewMoniker().ToStringId(Context.StringTable), new ClientConfig()),
                "asdf",
                CreateProvenance(),
                CreateOutputFileArtifact());
            PipGraphBuilder.AddIpcPip(p2);
            AssertOutputFilterMatches(p2, p2.OutputFile, CreateOutputFileArtifact());
        }

        [Feature(Features.OpaqueDirectory)]
        [Fact]
        public void TestFilterWithOnlySingleOutputDirectory()
        {
            AbsolutePath outputDirectoryPath = CreateUniqueObjPath("D");
            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new FileArtifact[0],
                outputDirectoryPaths: new[] {outputDirectoryPath},
                tags: new[] {"T1", "T2", "T3", "T4"});

            XAssert.IsTrue(IsFilterMatch(CreateTagFilter("T2"), p1));
            XAssert.IsFalse(IsFilterMatch(CreateTagFilter("asdf"), p1));
            XAssert.IsTrue(IsFilterMatch(CreateTagFilter("asdf").Negate(), p1));
        }

        [Fact]
        
        public void TestCanonFilterWithBinaryFilter()
        {
            var o1 = CreateOutputFileArtifact();
            var o2 = CreateOutputFileArtifact();
            var o3 = CreateOutputFileArtifact();
            var o4 = CreateOutputFileArtifact();

            var p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {o1},
                tags: new[] {"A"});

            var p2 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {o2},
                tags: new[] {"A", "B"});

            var p3 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {o3},
                tags: new[] {"A", "C"});

            var p4 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {o4},
                tags: new[] {"A", "D"});

            var graph = PipGraphBuilder.Build();
            Assert.NotNull(graph);

            var filter = new BinaryFilter(
                new BinaryFilter(CreateTagFilter("A"), FilterOperator.And, CreateTagFilter("B")),
                FilterOperator.Or,
                new BinaryFilter(CreateTagFilter("C"), FilterOperator.And, CreateTagFilter("A")));

            var actuals = graph.FilterOutputs(new RootFilter(filter));
            AssertSetEquals(new HashSet<FileOrDirectoryArtifact> { FileOrDirectoryArtifact.Create(o2), FileOrDirectoryArtifact.Create(o3) }, actuals);
        }

        [Fact]
        public void TestDependentsFilter()
        {
            var o1 = CreateOutputFileArtifact();
            var o2 = CreateOutputFileArtifact();
            var o3 = CreateOutputFileArtifact();
            var outputOfProcessInSpecOfP1 = CreateOutputFileArtifact();

            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {o1});

            Process processInSpecOfP1 = CreateAndScheduleProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { outputOfProcessInSpecOfP1 },
                provenance: CreateProvenance(specPath: p1.Provenance.Token.Path));

            Process p2 = CreateAndScheduleProcess(
                dependencies: new[] {o1},
                outputs: new[] {o2});

            var seal = CreateAndScheduleSealDirectory(o2.Path.GetParent(Context.PathTable), SealDirectoryKind.Partial, o2);

            Process p3 = CreateAndScheduleProcess(
                dependencies: new FileArtifact[] {},
                directoryDependencies: new[] {seal.Directory},
                outputs: new[] {o3});

            var graph = PipGraphBuilder.Build();

            var dependentsFilter =
                new RootFilter(
                    new DependentsFilter(
                        new SpecFileFilter(p2.Provenance.Token.Path, null, MatchMode.FilePath, false, false, specDependencies: false)));
            var actualDependentsOutputs = graph.FilterOutputs(dependentsFilter);

            var expectedDependentsOutputs = new HashSet<FileOrDirectoryArtifact>
                                               {
                                                   FileOrDirectoryArtifact.Create(o2),
                                                   FileOrDirectoryArtifact.Create(seal.Directory),
                                                   FileOrDirectoryArtifact.Create(o3),
                                               };
            AssertSetEquals(expectedDependentsOutputs, actualDependentsOutputs);
        }

        [Fact]
        public void TestCopyDependentsFilter()
        {
            // P1 -> P2 -> Seal -> P3 --> Copy3
            // |      +--> Copy2a -> Copy2b
            // +-> Copy1

            var o1 = CreateOutputFileArtifact();
            var o2 = CreateOutputFileArtifact();
            var o3 = CreateOutputFileArtifact();

            var c1 = CreateOutputFileArtifact();
            var c2a = CreateOutputFileArtifact();
            var c2b = CreateOutputFileArtifact();
            var c3 = CreateOutputFileArtifact();

            var outputOfProcessInSpecOfP1 = CreateOutputFileArtifact();

            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { o1 });

            Process processInSpecOfP1 = CreateAndScheduleProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { outputOfProcessInSpecOfP1 },
                provenance: CreateProvenance(specPath: p1.Provenance.Token.Path));

            Process p2 = CreateAndScheduleProcess(
                dependencies: new[] { o1 },
                outputs: new[] { o2 });

            var seal = CreateAndScheduleSealDirectory(o2.Path.GetParent(Context.PathTable), SealDirectoryKind.Partial, o2);

            Process p3 = CreateAndScheduleProcess(
                dependencies: new FileArtifact[] { },
                directoryDependencies: new[] { seal.Directory },
                outputs: new[] { o3 });

            // Add copies
            CreateAndScheduleCopyFile(o1, c1);
            CreateAndScheduleCopyFile(o2, c2a);
            CreateAndScheduleCopyFile(c2a, c2b);
            CreateAndScheduleCopyFile(o3, c3);

            var graph = PipGraphBuilder.Build();

            var dependentsFilter =
                new RootFilter(
                    new CopyDependentsFilter(
                        new SpecFileFilter(p2.Provenance.Token.Path, null, MatchMode.FilePath, false, false, specDependencies: false)));
            var actualDependentsOutputs = graph.FilterOutputs(dependentsFilter);

            var expectedDependentsOutputs = new HashSet<FileOrDirectoryArtifact>
                                               {
                                                   FileOrDirectoryArtifact.Create(o2),
                                                   FileOrDirectoryArtifact.Create(c2a),
                                                   FileOrDirectoryArtifact.Create(c2b),
                                               };
            AssertSetEquals(expectedDependentsOutputs, actualDependentsOutputs);
        }

        [Fact]
        public void TestDependenciesFilter()
        {
            // P0 -> P1 -> P2 -> Seal -> P3

            FileArtifact o0, o1, o2;
            Process p2;
            PipGraph graph;
            SetupDependenciesFilterGraph(out o0, out o1, out o2, out p2, out graph);

            var filter =
                new RootFilter(
                    new DependenciesFilter(
                        new SpecFileFilter(p2.Provenance.Token.Path, null, MatchMode.FilePath, false, false, specDependencies: false)));
            var actualDependentsOutputs = graph.FilterOutputs(filter);

            var expectedDependencyOutputs = new HashSet<FileOrDirectoryArtifact>
                                               {
                                                   FileOrDirectoryArtifact.Create(o0),
                                                   FileOrDirectoryArtifact.Create(o1),
                                                   FileOrDirectoryArtifact.Create(o2),
                                               };
            AssertSetEquals(expectedDependencyOutputs, actualDependentsOutputs);
        }

        [Fact]
        public void TestDependenciesMinusDependentsFilter()
        {
            // P0 -> P1 -> P2 -> Seal -> P3

            FileArtifact o0, o1, o2;
            Process p2;
            PipGraph graph;
            SetupDependenciesFilterGraph(out o0, out o1, out o2, out p2, out graph);

            var filter =
                new RootFilter(
                    new BinaryFilter(
                        new DependenciesFilter(
                            new SpecFileFilter(p2.Provenance.Token.Path, null, MatchMode.FilePath, false, false, specDependencies: false)),
                        FilterOperator.And,
                        new NegatingFilter(
                            new DependentsFilter(new SpecFileFilter(p2.Provenance.Token.Path, null, MatchMode.FilePath, false, false, specDependencies: false))
                        )
                    )
                );

            var actualDependentsOutputs = graph.FilterOutputs(filter);

            var expectedDependencyOutputs = new HashSet<FileOrDirectoryArtifact>
                                               {
                                                   FileOrDirectoryArtifact.Create(o0),
                                                   FileOrDirectoryArtifact.Create(o1),
                                               };
            AssertSetEquals(expectedDependencyOutputs, actualDependentsOutputs);
        }

        [Fact]
        public void TestRequiredInputsFilter()
        {
            // P0 -> P1 -> P2 -> Seal -> P3

            FileArtifact o0, o1, o2;
            Process p2;
            PipGraph graph;
            SetupDependenciesFilterGraph(out o0, out o1, out o2, out p2, out graph);

            var filter =
                new RootFilter(
                    new DependenciesFilter(
                        new SpecFileFilter(p2.Provenance.Token.Path, null, MatchMode.FilePath, false, false, specDependencies: false),
                        ClosureMode.DirectExcludingSelf));
            var actualDependentsOutputs = graph.FilterOutputs(filter);

            var expectedDependencyOutputs = new HashSet<FileOrDirectoryArtifact>
                                               {
                                                   FileOrDirectoryArtifact.Create(o1),
                                               };
            AssertSetEquals(expectedDependencyOutputs, actualDependentsOutputs);
        }

        [Theory]
        [InlineData(ClosureMode.DirectExcludingSelf)]
        [InlineData(ClosureMode.TransitiveIncludingSelf)]
        public void TestMultipleRequiredInputsFilter(ClosureMode closureMode)
        {
            // P0 -> P1 -> P2 -> Seal -> P3 (P3 doesn't matter in this test case, just to reuse the existing SetupDependenciesFilterGraph funcion)
            FileArtifact o0, o1, o2;
            Process p1, p2;
            PipGraph graph;
            SetupSimpleDependenciesFilterGraph(out o0, out o1, out o2, out p1, out p2, out graph);

            var filter =
                new RootFilter(
                    new DependenciesFilter(
                        new BinaryFilter(new SpecFileFilter(p2.Provenance.Token.Path, null, MatchMode.FilePath, false, false, specDependencies: false), FilterOperator.Or, new SpecFileFilter(p1.Provenance.Token.Path, null, MatchMode.FilePath, false, false, specDependencies: false)),
                        closureMode));
            var actualDependentsOutputs = graph.FilterOutputs(filter);
            HashSet<FileOrDirectoryArtifact> expectedDependencyOutputs;
            if (closureMode == ClosureMode.DirectExcludingSelf)
            {
                expectedDependencyOutputs = new HashSet<FileOrDirectoryArtifact>
                                               {
                                                   FileOrDirectoryArtifact.Create(o0),
                                               };
            }
            else
            {
                expectedDependencyOutputs = new HashSet<FileOrDirectoryArtifact>
                                               {
                                                   FileOrDirectoryArtifact.Create(o0),
                                                   FileOrDirectoryArtifact.Create(o1),
                                                   FileOrDirectoryArtifact.Create(o2),
                                               };
            }

            AssertSetEquals(expectedDependencyOutputs, actualDependentsOutputs);
        }

        private void SetupDependenciesFilterGraph(out FileArtifact o0, out FileArtifact o1, out FileArtifact o2, out Process p2, out PipGraph graph)
        {
            o0 = CreateOutputFileArtifact();
            o1 = CreateOutputFileArtifact();
            o2 = CreateOutputFileArtifact();
            var o3 = CreateOutputFileArtifact();
            var outputOfProcessInSpecOfP1 = CreateOutputFileArtifact();

            Process p0 = CreateAndScheduleProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { o0 });

            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] { o0 },
                outputs: new[] { o1 });

            Process processInSpecOfP1 = CreateAndScheduleProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { outputOfProcessInSpecOfP1 },
                provenance: CreateProvenance(specPath: p1.Provenance.Token.Path));

            p2 = CreateAndScheduleProcess(
                dependencies: new[] { o1 },
                outputs: new[] { o2 });

            var seal = CreateAndScheduleSealDirectory(o2.Path.GetParent(Context.PathTable), SealDirectoryKind.Partial, o2);

            Process p3 = CreateAndScheduleProcess(
                dependencies: new FileArtifact[] { },
                directoryDependencies: new[] { seal.Directory },
                outputs: new[] { o3 });

            graph = PipGraphBuilder.Build();
        }
        
        private void SetupSimpleDependenciesFilterGraph(out FileArtifact o0, out FileArtifact o1, out FileArtifact o2, out Process p1, out Process p2, out PipGraph graph)
        {
            o0 = CreateOutputFileArtifact();
            o1 = CreateOutputFileArtifact();
            o2 = CreateOutputFileArtifact();
            var o3 = CreateOutputFileArtifact();

            Process p0 = CreateAndScheduleProcess(
                dependencies: new[] { CreateSourceFile() },
                outputs: new[] { o0 });

            p1 = CreateAndScheduleProcess(
                dependencies: new[] { o0 },
                outputs: new[] { o1 });

            p2 = CreateAndScheduleProcess(
                dependencies: new[] { o1 },
                outputs: new[] { o2 });

            var seal = CreateAndScheduleSealDirectory(o2.Path.GetParent(Context.PathTable), SealDirectoryKind.Partial, o2);

            Process p3 = CreateAndScheduleProcess(
                dependencies: new FileArtifact[] { },
                directoryDependencies: new[] { seal.Directory },
                outputs: new[] { o3 });

            graph = PipGraphBuilder.Build();
        }

        [Feature(Features.SealedSourceDirectory)]
        [Fact]
        public void TestInputFilterOnAllDirectoriesSealedSourceDirectory()
        {
            var o = CreateOutputFileArtifact();
            var ssdAllPath = CreateUniqueSourcePath($"SSD-All{Path.DirectorySeparatorChar}a{Path.DirectorySeparatorChar}b");
            var ssdAll = CreateAndScheduleSealDirectory(ssdAllPath, SealDirectoryKind.SourceAllDirectories);

            var p = CreateAndScheduleProcess(
                dependencies: new FileArtifact[0],
                directoryDependencies: new[] {ssdAll.Directory},
                outputs: new[] {o});

            var graph = PipGraphBuilder.Build();
            var expectedMatch = new HashSet<FileOrDirectoryArtifact> { o };
            var expectedEmpty = new HashSet<FileOrDirectoryArtifact>();

            // Filter FilePath: SSD-All\a\b\c\d.
            var inputFilter1 = new InputFileFilter(
                ssdAllPath.Combine(Context.PathTable, RelativePath.Create(Context.StringTable, $"c{Path.DirectorySeparatorChar}d")),
                null,
                MatchMode.FilePath,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter1)));

            // Filter WithinDirectory: SSD-All\a.
            var inputFilter2 = new InputFileFilter(
                ssdAllPath.GetParent(Context.PathTable),
                null,
                MatchMode.WithinDirectory,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter2)));

            // Filter PathPrefixWildcard: *\b.
            var inputFilter3 = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{Path.DirectorySeparatorChar}{ssdAllPath.GetName(Context.PathTable).ToString(Context.StringTable)}"),
                MatchMode.PathPrefixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter3)));

            // Filter PathSuffixWildcard: SSD-All\a\b\c\*.
            var inputFilter4 = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{ssdAllPath.ToString(Context.PathTable)}{Path.DirectorySeparatorChar}c{Path.DirectorySeparatorChar}"),
                MatchMode.PathSuffixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter4)));

            // Filter PathSuffixWildcard: SSD-All\*.
            var inputFilter5 = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{ssdAllPath.GetParent(Context.PathTable).GetParent(Context.PathTable).ToString(Context.PathTable)}{Path.DirectorySeparatorChar}"),
                MatchMode.PathSuffixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter5)));

            // Filter FilePath: SSD-All\a
            var inputFilter6 = new InputFileFilter(
                ssdAllPath.GetParent(Context.PathTable),
                null,
                MatchMode.FilePath,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(inputFilter6)));

            // Filter PathPrefixWildcard: *\e.
            var inputFilter7 = new InputFileFilter(
                AbsolutePath.Invalid,
                $"{Path.DirectorySeparatorChar}e",
                MatchMode.PathPrefixWildcard,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(inputFilter7)));
        }

        [Fact]
        public void TestInputFilter()
        {
            FileArtifact input1 = CreateSourceFile();
            FileArtifact input2 = CreateSourceFile();
            SealDirectory sd = CreateAndScheduleSealDirectory(input1.Path.GetParent(Context.PathTable), SealDirectoryKind.Partial, input1, input2);
            FileArtifact input3 = CreateSourceFile();
            FileArtifact notAnInput = CreateSourceFile();

            FileArtifact output = CreateOutputFileArtifact();
            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {input3},
                directoryDependencies: new[] {sd.Directory},
                outputs: new[] {output},
                tags: new[] {"T1"});

            // Filter on a direct input of the process pip
            XAssert.IsTrue(IsFilterMatch(new InputFileFilter(input3.Path, null, MatchMode.FilePath, pathFromMount: false), p1));

            // Filter on a member of the seal directory
            var filter = new InputFileFilter(input1.Path, null, MatchMode.FilePath, pathFromMount: false);
            var outputs = filter.FilterOutputs(
                new PipFilterContext(
                    Context.PathTable,
                    allPips: new[] {p1.PipId, sd.PipId},
                    pipHydrator: pipId =>
                                 {
                                     if (pipId == sd.PipId)
                                     {
                                         return sd;
                                     }

                                     Contract.Assert(pipId == p1.PipId);
                                     return p1;
                                 },
                    pipDependenciesGetter: pipId => Enumerable.Empty<PipId>()));
            XAssert.IsTrue(outputs.Any(), "Should have matched p1 based on the seal directory members");

            // filter on a file that isn't an input
            XAssert.IsFalse(IsFilterMatch(new InputFileFilter(notAnInput.Path, null, MatchMode.FilePath, pathFromMount: false), p1));
        }

        [Fact]
        public void TestSpecDependenciesFilter()
        {
            var o1 = CreateOutputFileArtifact();
            var o2 = CreateOutputFileArtifact();
            var o3 = CreateOutputFileArtifact();
            var outputOfProcessInSpecOfP1 = CreateOutputFileArtifact();

            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {o1});

            Process processInSpecOfP1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {outputOfProcessInSpecOfP1},
                provenance: CreateProvenance(specPath: p1.Provenance.Token.Path));

            Process p2 = CreateAndScheduleProcess(
                dependencies: new[] {o1},
                outputs: new[] {o2});

            Process p3 = CreateAndScheduleProcess(
                dependencies: new[] {o2},
                outputs: new[] {o3});

            var s3 = p3.Provenance.Token.Path;

            var graph = PipGraphBuilder.Build();

            var filterWithoutSpecDependencies = new RootFilter(new SpecFileFilter(s3, null, MatchMode.FilePath, false, false, specDependencies: false));
            var actualWithoutSpecDependencies = graph.FilterOutputs(filterWithoutSpecDependencies);

            var expectedWithoutSpecDependencies = new HashSet<FileOrDirectoryArtifact> { FileOrDirectoryArtifact.Create(o3) };
            AssertSetEquals(expectedWithoutSpecDependencies, actualWithoutSpecDependencies);

            var filterWithSpecDependencies = new RootFilter(new SpecFileFilter(s3, null, MatchMode.FilePath, false, false, specDependencies: true));
            var actualWithSpecDependencies = graph.FilterOutputs(filterWithSpecDependencies);

            var expectedWithSpecDependencies = new HashSet<FileOrDirectoryArtifact>
                                               {
                                                   FileOrDirectoryArtifact.Create(o1),
                                                   FileOrDirectoryArtifact.Create(o2),
                                                   FileOrDirectoryArtifact.Create(o3),
                                                   FileOrDirectoryArtifact.Create(outputOfProcessInSpecOfP1)
                                               };
            AssertSetEquals(expectedWithSpecDependencies, actualWithSpecDependencies);
        }

        [Feature(Features.SealedSourceDirectory)]
        [Fact]
        public void TestInputFilterOnTopDirectoryOnlySealedSourceDirectory()
        {
            var o = CreateOutputFileArtifact();
            var ssdTopPath = CreateUniqueSourcePath($"SSD-Top{Path.DirectorySeparatorChar}a{Path.DirectorySeparatorChar}b");
            var ssdTop = CreateAndScheduleSealDirectory(ssdTopPath, SealDirectoryKind.SourceTopDirectoryOnly);

            var p = CreateAndScheduleProcess(
                dependencies: new FileArtifact[0],
                directoryDependencies: new[] {ssdTop.Directory},
                outputs: new[] {o});

            var graph = PipGraphBuilder.Build();
            var expectedMatch = new HashSet<FileOrDirectoryArtifact> {o};
            var expectedEmpty = new HashSet<FileOrDirectoryArtifact>();

            // Filter FilePath: SSD-Top\a\b\c.
            var inputFilter1Ok = new InputFileFilter(
                ssdTopPath.Combine(Context.PathTable, RelativePath.Create(Context.StringTable, "c")),
                null,
                MatchMode.FilePath,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter1Ok)));

            // Filter FilePath: SSD-Top\a\b\c\d.
            var inputFilter1NotOk = new InputFileFilter(
                ssdTopPath.Combine(Context.PathTable, RelativePath.Create(Context.StringTable, $"c{Path.DirectorySeparatorChar}d")),
                null,
                MatchMode.FilePath,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(inputFilter1NotOk)));

            // Filter WithinDirectory: SSD-Top\a.
            var inputFilter2 = new InputFileFilter(
                ssdTopPath.GetParent(Context.PathTable),
                null,
                MatchMode.WithinDirectory,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter2)));

            // Filter PathPrefixWildcard: *\b.
            var inputFilter3 = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{Path.DirectorySeparatorChar}{ssdTopPath.GetName(Context.PathTable).ToString(Context.StringTable)}"),
                MatchMode.PathPrefixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter3)));

            // Filter PathSuffixWildcard: SSD-Top\a\b\c\*.
            var inputFilter4NotOk = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{ssdTopPath.ToString(Context.PathTable)}{Path.DirectorySeparatorChar}c{Path.DirectorySeparatorChar}"),
                MatchMode.PathSuffixWildcard,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(inputFilter4NotOk)));

            // Filter PathSuffixWildcard: SSD-Top\a\b\*.
            var inputFilter4Ok = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{ssdTopPath.ToString(Context.PathTable)}{Path.DirectorySeparatorChar}"),
                MatchMode.PathSuffixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter4Ok)));

            // Filter PathSuffixWildcard: SSD-Top\*.
            var inputFilter5 = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{ssdTopPath.GetParent(Context.PathTable).GetParent(Context.PathTable).ToString(Context.PathTable)}{Path.DirectorySeparatorChar}"),
                MatchMode.PathSuffixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter5)));

            // Filter FilePath: SSD-Top\a
            var inputFilter6 = new InputFileFilter(
                ssdTopPath.GetParent(Context.PathTable),
                null,
                MatchMode.FilePath,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(inputFilter6)));

            // Filter PathPrefixWildcard: *\e.
            var inputFilter7 = new InputFileFilter(
                AbsolutePath.Invalid,
                $"{Path.DirectorySeparatorChar}e",
                MatchMode.PathPrefixWildcard,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(inputFilter7)));
        }

        [Feature(Features.OpaqueDirectory)]
        [Fact]
        public void TestInputFilterOnOutputDirectory()
        {
            var o = CreateOutputFileArtifact();
            var outputDirectoryPath = CreateUniqueObjPath($"OutputDir{Path.DirectorySeparatorChar}a{Path.DirectorySeparatorChar}b");
            var resultingSealedOutputDirectories = new Dictionary<AbsolutePath, DirectoryArtifact>();

            var q = CreateAndScheduleProcess(
                dependencies: new FileArtifact[0],
                outputs: new FileArtifact[0],
                outputDirectoryPaths: new[] {outputDirectoryPath});

            var p = CreateAndScheduleProcess(
                dependencies: new FileArtifact[0],
                directoryDependencies: new[] {OutputDirectory.Create(outputDirectoryPath)},
                outputs: new[] {o});

            var graph = PipGraphBuilder.Build();
            var expectedMatch = new HashSet<FileOrDirectoryArtifact> {o};
            var expectedEmpty = new HashSet<FileOrDirectoryArtifact>();

            // Filter FilePath: OutputDir\a\b\c\d.
            var inputFilter1 = new InputFileFilter(
                outputDirectoryPath.Combine(Context.PathTable, RelativePath.Create(Context.StringTable, $"c{Path.DirectorySeparatorChar}d")),
                null,
                MatchMode.FilePath,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter1)));

            // Filter WithinDirectory: OutputDir\a.
            var inputFilter2 = new InputFileFilter(
                outputDirectoryPath.GetParent(Context.PathTable),
                null,
                MatchMode.WithinDirectory,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter2)));

            // Filter PathPrefixWildcard: *\b.
            var inputFilter3 = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{Path.DirectorySeparatorChar}{outputDirectoryPath.GetName(Context.PathTable).ToString(Context.StringTable)}"),
                MatchMode.PathPrefixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter3)));

            // Filter PathSuffixWildcard: OutputDir\a\b\c\*.
            var inputFilter4 = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{outputDirectoryPath.ToString(Context.PathTable)}{Path.DirectorySeparatorChar}c{Path.DirectorySeparatorChar}"),
                MatchMode.PathSuffixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter4)));

            // Filter PathSuffixWildcard: OutputDir\*.
            var inputFilter5 = new InputFileFilter(
                AbsolutePath.Invalid,
                I($"{outputDirectoryPath.GetParent(Context.PathTable).GetParent(Context.PathTable).ToString(Context.PathTable)}{Path.DirectorySeparatorChar}"),
                MatchMode.PathSuffixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(inputFilter5)));

            // Filter FilePath: OutputDir\a
            var inputFilter6 = new InputFileFilter(
                outputDirectoryPath.GetParent(Context.PathTable),
                null,
                MatchMode.FilePath,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(inputFilter6)));

            // Filter PathPrefixWildcard: *\e.
            var inputFilter7 = new InputFileFilter(
                AbsolutePath.Invalid,
                $"{Path.DirectorySeparatorChar}e",
                MatchMode.PathPrefixWildcard,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(inputFilter7)));
        }

        [Feature(Features.OpaqueDirectory)]
        [Fact]
        public void TestOutputFilterOnOutputDirectory()
        {
            var outputDirectoryPath = CreateUniqueObjPath($"OutputDir{Path.DirectorySeparatorChar}a{Path.DirectorySeparatorChar}b");
            var resultingSealedOutputDirectories = new Dictionary<AbsolutePath, DirectoryArtifact>();

            var p = CreateAndScheduleProcess(
                dependencies: new FileArtifact[0],
                outputs: new FileArtifact[0],
                outputDirectoryPaths: new[] {outputDirectoryPath});

            var o = OutputDirectory.Create(outputDirectoryPath);

            var graph = PipGraphBuilder.Build();
            var expectedMatch = new HashSet<FileOrDirectoryArtifact> {o};
            var expectedEmpty = new HashSet<FileOrDirectoryArtifact>();

            // Filter FilePath: OutputDir\a\b\c\d.
            var outputFilter1 = new OutputFileFilter(
                outputDirectoryPath.Combine(Context.PathTable, RelativePath.Create(Context.StringTable, $"c{Path.DirectorySeparatorChar}d")),
                null,
                MatchMode.FilePath,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(outputFilter1)));

            // Filter WithinDirectory: OutputDir\a.
            var outputFilter2 = new OutputFileFilter(
                outputDirectoryPath.GetParent(Context.PathTable),
                null,
                MatchMode.WithinDirectory,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(outputFilter2)));

            // Filter PathPrefixWildcard: *\b.
            var outputFilter3 = new OutputFileFilter(
                AbsolutePath.Invalid,
                I($"{Path.DirectorySeparatorChar}{outputDirectoryPath.GetName(Context.PathTable).ToString(Context.StringTable)}"),
                MatchMode.PathPrefixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(outputFilter3)));

            // Filter PathSuffixWildcard: OutputDir\a\b\c\*.
            var outputFilter4 = new OutputFileFilter(
                AbsolutePath.Invalid,
                I($"{outputDirectoryPath.ToString(Context.PathTable)}{Path.DirectorySeparatorChar}c{Path.DirectorySeparatorChar}"),
                MatchMode.PathSuffixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(outputFilter4)));

            // Filter PathSuffixWildcard: OutputDir\*.
            var outputFilter5 = new OutputFileFilter(
                AbsolutePath.Invalid,
                I($"{outputDirectoryPath.GetParent(Context.PathTable).GetParent(Context.PathTable).ToString(Context.PathTable)}{Path.DirectorySeparatorChar}"),
                MatchMode.PathSuffixWildcard,
                false);
            AssertSetEquals(expectedMatch, graph.FilterOutputs(new RootFilter(outputFilter5)));

            // Filter FilePath: OutputDir\a
            var outputFilter6 = new OutputFileFilter(
                outputDirectoryPath.GetParent(Context.PathTable),
                null,
                MatchMode.FilePath,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(outputFilter6)));

            // Filter PathPrefixWildcard: *\e.
            var outputFilter7 = new OutputFileFilter(
                AbsolutePath.Invalid,
                $"{Path.DirectorySeparatorChar}e",
                MatchMode.PathPrefixWildcard,
                false);
            AssertSetEquals(expectedEmpty, graph.FilterOutputs(new RootFilter(outputFilter7)));
        }

        [Fact]
        public void TestMultiTagsFilter()
        {
            var output1 = CreateOutputFileArtifact();
            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output1},
                tags: new[] {"A"});

            var output2 = CreateOutputFileArtifact();
            Process p2 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output2},
                tags: new[] {"B"});

            var output3 = CreateOutputFileArtifact();
            Process p3 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output3},
                tags: new[] {"C"});

            var graph = PipGraphBuilder.Build();
            XAssert.IsNotNull(graph);

            // Filter: MultiTags(A, B).
            var filter = CreateMultiTagsFilter("A", "B");
            var actual = graph.FilterOutputs(new RootFilter(filter));
            AssertSetEquals(new HashSet<FileOrDirectoryArtifact> {output1, output2}, actual);
        }

        [Fact]
        public void TestCombinedBinaryAndAndInputFilter()
        {
            FileArtifact input1 = CreateSourceFile();
            FileArtifact input2 = CreateSourceFile();
            SealDirectory sd = CreateAndScheduleSealDirectory(input1.Path.GetParent(Context.PathTable), SealDirectoryKind.Partial, input1, input2);
            FileArtifact input3 = CreateSourceFile();

            FileArtifact output1 = CreateOutputFileArtifact();
            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {input3},
                directoryDependencies: new[] {sd.Directory},
                outputs: new[] {output1},
                tags: new[] {"T1"});

            FileArtifact output2 = CreateOutputFileArtifact();
            Process p2 = CreateAndScheduleProcess(
                dependencies: new[] {input3},
                outputs: new[] {output2},
                tags: new[] {"T2"});

            var graph = PipGraphBuilder.Build();
            XAssert.IsNotNull(graph);

            var inputFilter = new InputFileFilter(input3.Path, null, MatchMode.FilePath, pathFromMount: false);
            var andFilter = new BinaryFilter(CreateTagFilter("T1"), FilterOperator.And, inputFilter);
            var actual = graph.FilterOutputs(new RootFilter(andFilter));
            AssertSetEquals(new HashSet<FileOrDirectoryArtifact> {output1}, actual);
        }

        [Fact]
        public void TestCombinedBinaryAndAndNegatingFilter()
        {
            var output1 = CreateOutputFileArtifact();
            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output1},
                tags: new[] {"A"});

            var output2 = CreateOutputFileArtifact();
            Process p2 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output2},
                tags: new[] {"B"});

            var graph = PipGraphBuilder.Build();
            XAssert.IsNotNull(graph);

            // Filter: !(A /\ B).
            var filter = new BinaryFilter(CreateTagFilter("A"), FilterOperator.And, CreateTagFilter("B")).Negate();
            var actual = graph.FilterOutputs(new RootFilter(filter));
            AssertSetEquals(new HashSet<FileOrDirectoryArtifact> {output1, output2}, actual);
        }

        [Fact]
        public void TestCombinedBinaryOrAndNegatingFilter()
        {
            var output1 = CreateOutputFileArtifact();
            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output1},
                tags: new[] {"A"});

            var output2 = CreateOutputFileArtifact();
            Process p2 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output2},
                tags: new[] {"B"});

            var output3 = CreateOutputFileArtifact();
            Process p3 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {output3},
                tags: new[] {"C"});

            var graph = PipGraphBuilder.Build();
            XAssert.IsNotNull(graph);

            // Filter: !(A \/ B).
            var filter = new BinaryFilter(CreateTagFilter("A"), FilterOperator.Or, CreateTagFilter("B")).Negate();
            ;
            var actual = graph.FilterOutputs(new RootFilter(filter));
            AssertSetEquals(new HashSet<FileOrDirectoryArtifact> {output3}, actual);
        }

        [Fact]
        public void TestCombinedBinaryAndAndDependentsFilter()
        {
            var o1 = CreateOutputFileArtifact();
            var o2 = CreateOutputFileArtifact();
            var o3 = CreateOutputFileArtifact();
            var outputOfProcessInSpecOfP1 = CreateOutputFileArtifact();

            Process p1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {o1});

            Process processInSpecOfP1 = CreateAndScheduleProcess(
                dependencies: new[] {CreateSourceFile()},
                outputs: new[] {outputOfProcessInSpecOfP1},
                provenance: CreateProvenance(specPath: p1.Provenance.Token.Path));

            Process p2 = CreateAndScheduleProcess(
                dependencies: new[] {o1},
                outputs: new[] {o2},
                tags: new[] {"A"});

            var seal = CreateAndScheduleSealDirectory(o2.Path.GetParent(Context.PathTable), SealDirectoryKind.Partial, o2);

            Process p3 = CreateAndScheduleProcess(
                dependencies: new FileArtifact[] {},
                directoryDependencies: new[] {seal.Directory},
                outputs: new[] {o3},
                tags: new[] {"B"});

            var graph = PipGraphBuilder.Build();

            var dependentsFilter =
                new DependentsFilter(new SpecFileFilter(p2.Provenance.Token.Path, null, MatchMode.FilePath, false, false, specDependencies: false));
            var tagFilter = CreateTagFilter("A");
            var filter = new BinaryFilter(tagFilter.Negate(), FilterOperator.And, dependentsFilter);

            var actualDependentsOutputs = graph.FilterOutputs(new RootFilter(filter));

            var expectedDependentsOutputs = new HashSet<FileOrDirectoryArtifact>
                                               {
                                                   FileOrDirectoryArtifact.Create(seal.Directory),
                                                   FileOrDirectoryArtifact.Create(o3),
                                               };
            AssertSetEquals(expectedDependentsOutputs, actualDependentsOutputs);
        }

        internal static void AssertSetEquals<T>(HashSet<T> expected, IReadOnlySet<T> actual)
        {
            Assert.Equal(expected.Count, actual.Count);

            foreach (var expectedItem in expected)
            {
                Assert.True(actual.Contains(expectedItem));
            }
        }

        /// <summary>
        /// <paramref name="output"/> is expected to be an output of <paramref name="p1"/>.
        /// <paramref name="output"/> is expected not to be an output of <paramref name="p1"/>.
        /// </summary>
        private void AssertOutputFilterMatches(Pip p1, FileArtifact output, FileArtifact notOutput)
        {
            XAssert.IsTrue(IsFilterMatch(CreateOutputPathFilter(output.Path), p1));
            XAssert.IsTrue(IsFilterMatch(CreateOutputFileFilter(AbsolutePath.Invalid, output.Path.GetName(Context.PathTable).ToString(Context.StringTable), MatchMode.PathPrefixWildcard), p1));
            XAssert.IsTrue(IsFilterMatch(CreateOutputFileFilter(output.Path.GetParent(Context.PathTable), null, MatchMode.WithinDirectory), p1));
            XAssert.IsTrue(IsFilterMatch(CreateOutputFileFilter(output.Path.GetParent(Context.PathTable).GetParent(Context.PathTable), null, MatchMode.WithinDirectoryAndSubdirectories), p1));

            XAssert.IsFalse(IsFilterMatch(CreateOutputFileFilter(output.Path.GetParent(Context.PathTable).GetParent(Context.PathTable), null, MatchMode.WithinDirectory), p1));
            XAssert.IsFalse(IsFilterMatch(CreateOutputPathFilter(notOutput.Path), p1));
            XAssert.IsFalse(IsFilterMatch(CreateOutputFileFilter(AbsolutePath.Invalid, notOutput.Path.GetName(Context.PathTable).ToString(Context.StringTable), MatchMode.PathPrefixWildcard), p1));

            XAssert.IsTrue(IsFilterMatch(CreateOutputPathFilter(notOutput.Path).Negate(), p1));
        }

        private Process CreateAndScheduleProcess(
            FileArtifact[] dependencies,
            FileArtifact[] outputs,
            AbsolutePath[] outputDirectoryPaths = null,
            string[] tags = null,
            DirectoryArtifact[] directoryDependencies = null,
            PipProvenance provenance = null)
        {
            List<Operation> operations = new List<Operation>();
            foreach (var dependency in dependencies)
            {
                operations.Add(Operation.ReadFile(dependency));
            }

            foreach (var output in outputs)
            {
                operations.Add(Operation.WriteFile(output));
            }

            Process result = CreateProcess(
                operations,
                tags: tags,
                directoryOutputs: outputDirectoryPaths?.Select(OutputDirectory.Create),
                directoryDependencies: directoryDependencies,
                provenance: provenance);

            XAssert.IsTrue(PipGraphBuilder.AddProcess(result));
            return result;
        }

        private bool IsFilterMatch(PipFilter filter, Pip pip)
        {
            return filter.FilterOutputs(
                new PipFilterContext(
                    Context.PathTable,
                    allPips: new[] { pip.PipId },
                    pipHydrator: p => pip,
                    pipDependenciesGetter: p => Enumerable.Empty<PipId>(),
                    producerGetter: fd => pip.PipId)).Any();
        }

        private TagFilter CreateTagFilter(string tag)
        {
            return new TagFilter(StringId.Create(Context.PathTable.StringTable, tag));
        }

        private MultiTagsOrFilter CreateMultiTagsFilter(params string[] tags)
        {
            return new MultiTagsOrFilter(tags.Select(t => StringId.Create(Context.PathTable.StringTable, t)).ToArray());
        }

        private static OutputFileFilter CreateOutputPathFilter(AbsolutePath path)
        {
            return CreateOutputFileFilter(path, null, MatchMode.FilePath);
        }

        private static OutputFileFilter CreateOutputFileFilter(AbsolutePath path, string filenameWildcard, MatchMode matchMode)
        {
            return new OutputFileFilter(path, filenameWildcard, matchMode, false);
        }
    }
}

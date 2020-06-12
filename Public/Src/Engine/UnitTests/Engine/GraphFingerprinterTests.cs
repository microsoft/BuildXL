// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.Engine;
using BuildXL.Engine.Tracing;
using BuildXL.Pips.Filter;
using BuildXL.Storage;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using System.IO;
using BuildXL.Utilities.Collections;

namespace Test.BuildXL.Engine
{
    public class GraphFingerprinterTests : TemporaryStorageTestBase
    {
        private readonly PathTable m_pathTable;
        private readonly SymbolTable m_symbolTable;
        private readonly BuildXLContext m_context;

        /// <inheritdoc />
        public GraphFingerprinterTests()
        {
            m_context = BuildXLContext.CreateInstanceForTesting();
            m_pathTable = m_context.PathTable;
            m_symbolTable = new SymbolTable(m_pathTable.StringTable);
        }

        [Fact]
        public void FingerprintWithModuleSubSetIsCompatible()
        {
            var oldFingerprint = CreateRandomWithModules("A", "B");
            var newFingerprint = CreateRandomWithModules("A");
            var comparison = oldFingerprint.CompareFingerprint(newFingerprint);
            Assert.Equal(GraphCacheMissReason.NoMiss, comparison);
        }

        [Fact]
        public void FingerprintWithModuleDisjointSetIsNotCompatible()
        {
            var oldFingerprint = CreateRandomWithModules("A", "B");
            var newFingerprint = CreateRandomWithModules("B", "C");
            var comparison = oldFingerprint.CompareFingerprint(newFingerprint);
            Assert.Equal(GraphCacheMissReason.EvaluationFilterChanged, comparison);
        }

        [Fact]
        public void FingerprintWithModuleSubSetIsCompatibleIfValuesAreCompatible()
        {
            var oldFingerprint = CreateRandomWithModulesAndValues(modules: new []{"A", "B"}, valueNames: new []{"v"});
            var newFingerprint = CreateRandomWithModulesAndValues(modules: new[] { "A"}, valueNames: new[] { "v" });
            var comparison = oldFingerprint.CompareFingerprint(newFingerprint);
            Assert.Equal(GraphCacheMissReason.NoMiss, comparison);
        }

        [Fact]
        public void NewEmptyFingerprintIsCompatible()
        {
            var oldFingerprint = CreateRandomWithModulesAndValues(modules: new string[0], valueNames: new []{"v"});
            var newFingerprint = CreateRandomWithModulesAndValues(modules: new[]{ "A", "B"}, valueNames: new[] { "v" });
            var comparison = oldFingerprint.CompareFingerprint(newFingerprint);
            Assert.Equal(GraphCacheMissReason.NoMiss, comparison);
        }

        [Fact]
        public void OldEmptyFingerprintIsNotCompatible()
        {
            var oldFingerprint = CreateRandomWithModulesAndValues(modules: new []{"A", "B"}, valueNames: new []{"v"});
            var newFingerprint = CreateRandomWithModulesAndValues(modules: new string[0], valueNames: new[] { "v" });
            var comparison = oldFingerprint.CompareFingerprint(newFingerprint);
            Assert.Equal(GraphCacheMissReason.NoMiss, comparison);
        }

        [Fact]
        public void FingerprintWithModuleSubSetIsCompatibleIfValuesAreCompatible2()
        {
            var oldFingerprint = CreateRandomWithModulesAndValues(modules: new []{"A", "B"}, valueNames: new []{"v", "v2"});
            var newFingerprint = CreateRandomWithModulesAndValues(modules: new[] { "A"}, valueNames: new[] { "v" });
            var comparison = oldFingerprint.CompareFingerprint(newFingerprint);
            Assert.Equal(GraphCacheMissReason.NoMiss, comparison);
        }

        [Fact]
        public void FingerprintWithModuleDisjointSetIsNotCompatibleIfValuesAreNotCompatible()
        {
            var oldFingerprint = CreateRandomWithModulesAndValues(modules: new[] { "A", "B" }, valueNames: new[] { "v", "v2" });
            var newFingerprint = CreateRandomWithModulesAndValues(modules: new[] { "A", "B" }, valueNames: new[] { "v3" });
            var comparison = oldFingerprint.CompareFingerprint(newFingerprint);
            Assert.Equal(GraphCacheMissReason.EvaluationFilterChanged, comparison);
        }

        [Fact]
        public void FingerprintWithDifferentTopLevelHash()
        {
            var oldFingerprint = CreateRandomWithModulesAndValues(modules: new[] { "A", "B" }, valueNames: new[] { "v", "v2" });
            oldFingerprint.TopLevelHash = Fingerprint.Random(33);
            var newFingerprint = CreateRandomWithModulesAndValues(modules: new[] { "A", "B" }, valueNames: new[] { "v", "v2" });

            var comparison = oldFingerprint.CompareFingerprint(newFingerprint);
            Assert.Equal(GraphCacheMissReason.FingerprintChanged, comparison);

            newFingerprint.TopLevelHash = oldFingerprint.TopLevelHash;
            var comparison2 = oldFingerprint.CompareFingerprint(newFingerprint);
            Assert.Equal(GraphCacheMissReason.NoMiss, comparison2);
        }

        [Fact]
        public void GenerateHashWithDifferentTopLevelHash()
        {
            WriteFile("config.ds", "SampleConfig");
            var configPath = Path.Combine(TemporaryDirectory, "config.ds");

            var oldFingerprint = GenerateRandomTopLevelHash(configPath, "1", false);
            var newFingerprint1 = GenerateRandomTopLevelHash(configPath, "1", true);
            var newFingerprint2 = GenerateRandomTopLevelHash(configPath, "2", true);
            var newFingerprint3 = GenerateRandomTopLevelHash(configPath, "2", false);

            var comparison1 = oldFingerprint.CompareFingerprint(newFingerprint1);
            var comparison2 = oldFingerprint.CompareFingerprint(newFingerprint2);
            var comparison3 = oldFingerprint.CompareFingerprint(newFingerprint3);

            Assert.Equal(GraphCacheMissReason.FingerprintChanged, comparison1);
            Assert.Equal(GraphCacheMissReason.FingerprintChanged, comparison2);
            Assert.Equal(GraphCacheMissReason.FingerprintChanged, comparison3);
        }

        [Fact]
        public void GenerateHashWithSameTopLevelHash()
        {
            WriteFile("config.ds", "SampleConfig");
            var configPath = Path.Combine(TemporaryDirectory, "config.ds");

            var oldFingerprint1 = GenerateRandomTopLevelHash(configPath, "1", false);
            var newFingerprint1 = GenerateRandomTopLevelHash(configPath, "1", false);
            var comparison1 = oldFingerprint1.CompareFingerprint(newFingerprint1);
            Assert.Equal(GraphCacheMissReason.NoMiss, comparison1);

            var oldFingerprint2 = GenerateRandomTopLevelHash(configPath, "2", true);
            var newFingerprint2 = GenerateRandomTopLevelHash(configPath, "2", true);
            var comparison2 = oldFingerprint2.CompareFingerprint(newFingerprint2);
            Assert.Equal(GraphCacheMissReason.NoMiss, comparison2);
        }

        [Fact]
        public void GenerateHashWithDifferentEvaluationFilters()
        {
            WriteFile("config.ds", "SampleConfig");

            var context1 = BuildXLContext.CreateInstanceForTesting();

            var configuration1 = ConfigurationHelpers.GetDefaultForTesting(context1.PathTable, AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, "config.ds")));

            var evaluationFilter1 = new EvaluationFilter(
                                context1.SymbolTable,
                                context1.PathTable,
                                new FullSymbol[0],
                                new[]
                                {
                                    AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"testFile1.txt")),
                                },
                                CollectionUtilities.EmptyArray<StringId>());

            var evaluationFilter2 = new EvaluationFilter(
                                context1.SymbolTable,
                                context1.PathTable,
                                new FullSymbol[0],
                                new[]
                                {
                                    AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"testFile2.txt")),
                                },
                                CollectionUtilities.EmptyArray<StringId>());

            configuration1.Layout.ObjectDirectory = AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"ObjectDirectory1"));
            configuration1.Layout.TempDirectory = AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"TempDirectory1"));
            configuration1.Layout.SourceDirectory = AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"SourceDirectory1"));
            configuration1.Logging.SubstTarget = AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"SubstTarget1"));
            configuration1.Engine.CompressGraphFiles = false;
            configuration1.Schedule.SkipHashSourceFile = false;
            configuration1.Schedule.ComputePipStaticFingerprints = false;

            var loggingContext1 = CreateLoggingContextForTest();

            var fileContentTable1 = FileContentTable.CreateNew(loggingContext1);

            var oldFingerprint = GraphFingerprinter.TryComputeFingerprint(loggingContext1, configuration1.Startup, configuration1, context1.PathTable, evaluationFilter1, fileContentTable1, "111aaa", null).ExactFingerprint;
            var newFingerprint1 = GraphFingerprinter.TryComputeFingerprint(loggingContext1, configuration1.Startup, configuration1, context1.PathTable, evaluationFilter2, fileContentTable1, "111aaa", null).ExactFingerprint;
            
            var comparison = oldFingerprint.CompareFingerprint(newFingerprint1);

            Assert.Equal(GraphCacheMissReason.EvaluationFilterChanged, comparison);
        }

        private CompositeGraphFingerprint CreateRandomWithModules(params string[] modules)
        {
            return new CompositeGraphFingerprint()
                   {
                       OverallFingerprint = new ContentFingerprint(Fingerprint.Random(33)),
                       FilterHash = Fingerprint.Random(33),
                       EvaluationFilter = new EvaluationFilter(m_symbolTable, m_pathTable, new FullSymbol[0], new AbsolutePath[0], modules.Select(m => StringId.Create(this.m_pathTable.StringTable, m)).ToArray()),
                       BuildEngineHash = FingerprintUtilities.ZeroFingerprint,
                       ConfigFileHash = FingerprintUtilities.ZeroFingerprint,
                       QualifierHash = FingerprintUtilities.ZeroFingerprint,
                       TopLevelHash = FingerprintUtilities.ZeroFingerprint,
            };
        }

        private CompositeGraphFingerprint GenerateRandomTopLevelHash(string configPath, string index, bool flag)
        {
            var context1 = BuildXLContext.CreateInstanceForTesting();

            var configuration1 = ConfigurationHelpers.GetDefaultForTesting(context1.PathTable, AbsolutePath.Create(context1.PathTable, configPath));

            var evaluationFilter1 = new EvaluationFilter(
                                context1.SymbolTable,
                                context1.PathTable,
                                new FullSymbol[0],
                                new[]
                                {
                                    AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"testFile{index}.txt")),
                                },
                                CollectionUtilities.EmptyArray<StringId>());

            configuration1.Layout.ObjectDirectory = AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"ObjectDirectory{index}"));
            configuration1.Layout.TempDirectory = AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"TempDirectory{index}"));
            configuration1.Layout.SourceDirectory = AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"SourceDirectory{index}"));
            configuration1.Logging.SubstTarget = AbsolutePath.Create(context1.PathTable, Path.Combine(TemporaryDirectory, $"SubstTarget{index}"));
            configuration1.Engine.CompressGraphFiles = flag;
            configuration1.Schedule.SkipHashSourceFile = flag;
            configuration1.Schedule.ComputePipStaticFingerprints = flag;

            var loggingContext1 = CreateLoggingContextForTest();

            var fileContentTable1 = FileContentTable.CreateNew(loggingContext1);

            return GraphFingerprinter.TryComputeFingerprint(loggingContext1, configuration1.Startup, configuration1, context1.PathTable, evaluationFilter1, fileContentTable1, "111aaa", null).ExactFingerprint;
        }

        private CompositeGraphFingerprint CreateRandomWithModulesAndValues(string[] modules, string[] valueNames)
        {
            return new CompositeGraphFingerprint()
                   {
                       OverallFingerprint = new ContentFingerprint(Fingerprint.Random(33)),
                       FilterHash = Fingerprint.Random(33),
                       EvaluationFilter = new EvaluationFilter(
                           m_symbolTable,
                           m_pathTable,
                           valueNames.Select(vn => FullSymbol.Create(m_symbolTable, new StringSegment(vn))).ToArray(),
                           new AbsolutePath[0],
                           modules.Select(m => StringId.Create(m_pathTable.StringTable, m)).ToArray()),
                       BuildEngineHash = FingerprintUtilities.ZeroFingerprint,
                       ConfigFileHash = FingerprintUtilities.ZeroFingerprint,
                       QualifierHash = FingerprintUtilities.ZeroFingerprint,
                       TopLevelHash = FingerprintUtilities.ZeroFingerprint,
            };
        }
    }
}

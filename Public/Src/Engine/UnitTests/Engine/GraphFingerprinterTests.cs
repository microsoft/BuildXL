// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.Engine;
using BuildXL.Engine.Tracing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Xunit;

namespace Test.BuildXL.Engine
{
    public class GraphFingerprinterTests
    {
        private readonly PathTable m_pathTable;
        private readonly SymbolTable m_symbolTable;

        /// <inheritdoc />
        public GraphFingerprinterTests()
        {
            m_pathTable = new PathTable();
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
            };
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
                           valueNames.Select(vn => FullSymbol.Create(this.m_symbolTable, new StringSegment(vn))).ToArray(),
                           new AbsolutePath[0],
                           modules.Select(m => StringId.Create(this.m_pathTable.StringTable, m)).ToArray()),
                       BuildEngineHash = FingerprintUtilities.ZeroFingerprint,
                       ConfigFileHash = FingerprintUtilities.ZeroFingerprint,
                       QualifierHash = FingerprintUtilities.ZeroFingerprint,
            };
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Core.Incrementality;
using TypeScript.Net.Types;
using Xunit;

namespace Test.DScript.Ast.Incrementality
{
    public class TestSpecInteractionStatePersistence
    {
        private readonly PathTable m_pathTable;
        private readonly SpecInteractionStateEqualityComparer m_comparer;

        public TestSpecInteractionStatePersistence()
        {
            m_pathTable = new PathTable();
            m_comparer = new SpecInteractionStateEqualityComparer(m_pathTable);
        }

        [Fact]
        public void SerializeLargeArrayWithRandomFingerprints()
        {
            // Arrange

            const int SpecCount = 128;
            const int FingerprintSize = 128;
            var specs = 
                Enumerable.Range(1, SpecCount)
                .Select(n => Factories.CreateSourceFileWithRandomContent(FingerprintSize))
                .ToArray();

            // Act
            var snapshot = CreateSpecStateFrom(specs);
            var computedSnapshot = ComputeSnapshotFrom(specs);

            // Assert
            Assert.Equal(SpecCount, snapshot.Length);

            for (int i = 0; i < SpecCount; i++)
            {
                Assert.Equal(computedSnapshot[i], snapshot[i], m_comparer);
            }
        }

        private SpecBindingState[] CreateSpecStateFrom(ISourceFile[] specs)
        {
            using (var ms = new MemoryStream())
            {
                var buildXLWriter = new BuildXLWriter(true, ms, true, true);
                FrontEndSnapshotSerializer.SerializeWorkspaceBindingSnapshot(new SourceFileBasedBindingSnapshot(specs, m_pathTable), buildXLWriter, m_pathTable);

                ms.Position = 0;
                var reader = new BuildXLReader(true, ms, true);
                return FrontEndSnapshotSerializer.DeserializeSpecStates(reader, m_pathTable);
            }
        }

        private SpecBindingState[] ComputeSnapshotFrom(ISourceFile[] specs)
        {
            var result = new SpecBindingState[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var fullPath = spec.GetAbsolutePath(m_pathTable);
                spec.FileDependencies.MaterializeSet(m_pathTable);
                spec.FileDependents.MaterializeSet(m_pathTable);
                var fingerprint = spec.BindingSymbols.ReferencedSymbolsFingerprint;
                var declarationFingerprint = spec.BindingSymbols.DeclaredSymbolsFingerprint;

                Contract.Assert(fingerprint != null);

                result[i] = new SpecBindingState(fullPath, fingerprint, declarationFingerprint, spec.FileDependencies, spec.FileDependents);
            }

            return result;
        }
    }
}

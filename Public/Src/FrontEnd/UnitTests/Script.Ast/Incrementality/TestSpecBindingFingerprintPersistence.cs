// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Core.Incrementality;
using TypeScript.Net.Incrementality;
using Xunit;

namespace Test.DScript.Ast.Incrementality
{
    public class TestSpecBindingFingerprintPersistence
    {
        private readonly SymbolTable m_symbolTable = new SymbolTable();

        [Fact]
        public void SerializeLargeArrayWithRandomBitsSet()
        {
            // Arrange

            // Tests that relies on random data considered harmful, but in this particular case
            // it is safe.
            // Better way would be to use property-based tests instead.
            const int Length = 1024;
            var snapshot = Factories.CraeteFingerprintWithRandomContent(Length);

            // Act
            var copy = Copy(snapshot, m_symbolTable);

            // Assert
            Assert.Equal(snapshot, copy, SpecBindingFingerpintEqualityComparer.Instance);
        }

        private static SpecBindingSymbols Copy(SpecBindingSymbols symbols, SymbolTable symbolTable)
        {
            using (var ms = new MemoryStream())
            {
                BuildXLWriter writer = new BuildXLWriter(true, ms, true, true);
                FrontEndSnapshotSerializer.SerializeBindingSymbols(symbols, writer);

                ms.Position = 0;
                BuildXLReader reader = new BuildXLReader(true, ms, true);
                return FrontEndSnapshotSerializer.DeserializeBindingFingerprint(reader);
            }
        }
    }
}

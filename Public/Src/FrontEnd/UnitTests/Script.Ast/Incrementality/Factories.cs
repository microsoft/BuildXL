// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using TypeScript.Net.Core;
using TypeScript.Net.Incrementality;
using TypeScript.Net.Types;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.DScript.Ast.Incrementality
{
    internal static class Factories
    {
        private static readonly Random s_random = new Random(42);
        public static ISourceFile CreateSourceFileWithRandomContent(int fingerprintSize)
        {
            // It is ok to create an empty source file for the testing purposes.
            var result = new SourceFile();

            result.Path = Path.Absolute(A("c") + RandomString(24) + ".dsc");
            result.SetBindingFingerprintByTest(CraeteFingerprintWithRandomContent(fingerprintSize));
            result.SetFileDependentsByTest(CraeteBitArrayWithRandomContent(fingerprintSize));
            result.SetFileDependenciesByTest(CraeteBitArrayWithRandomContent(fingerprintSize));

            return result;
        }

        public static ConcurrentBitArray CraeteBitArrayWithRandomContent(int length)
        {
            var bitArray = new ConcurrentBitArray(length);

            for (int i = 0; i < length; i++)
            {
                if ((s_random.Next(100) % 2) == 0)
                {
                    bitArray.TrySet(i, true);
                }
            }

            return bitArray;
        }

        public static RoaringBitSet CreateBitSetWithRandomContent(int length)
        {
            return RoaringBitSet.FromBitArray(CraeteBitArrayWithRandomContent(length));
        }

        public static SpecBindingSymbols CraeteFingerprintWithRandomContent(int length)
        {
            var declarations = GenerateRandomSymbols(length / 2);
            var references = GenerateRandomSymbols(length / 2);

            return new SpecBindingSymbols(declarations.ToReadOnlySet(), references.ToReadOnlySet(), "a", "b");
        }

        private static List<InteractionSymbol> GenerateRandomSymbols(int length)
        {
            var symbols = new List<InteractionSymbol>(length);
            for (int i = 0; i < length; i++)
            {
                var stringLength = s_random.Next(5) + 3;
                var fullName = string.Join(".", Enumerable.Range(1, stringLength).Select(n => RandomString(n)));

                var symbolKind = (SymbolKind) s_random.Next((int) SymbolKind.Reference);
                symbols.Add(new InteractionSymbol(symbolKind, fullName));
            }
            return symbols;
        }

        private static readonly Random s_randomStringGenerator = new Random();
        public static string RandomString(int length)
        {
            const string Chars = "abcdefghijklmopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            return new string(Enumerable.Repeat(Chars, length)
              .Select(s => s[s_randomStringGenerator.Next(s.Length)]).ToArray());
        }
    }
}

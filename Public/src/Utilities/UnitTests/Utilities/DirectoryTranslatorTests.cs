// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using System.Collections.Generic;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class DirectoryTranslatorTests : XunitBuildXLTest
    {
        public DirectoryTranslatorTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // insert a null for drive if unix because we need to translate dirs and not drives
        private string[] getAtoms(string[] atoms)
        {
            List<string> list = new List<string>(atoms);
            if (OperatingSystemHelper.IsUnixOS)
            {
                list.Insert(0, null);
            }
            return list.ToArray();
        }

        [Fact]
        public void TestDirectoryTranslatorNoCycle1()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            DirectoryTranslator.RawInputTranslation[] translations = new[]
                               {
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "B" }), getAtoms(new string[] { "C" })),
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "A" }), getAtoms(new string[] { "B" })),
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "C", "foo", "bar" }), getAtoms(new string[] {"A"})),
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "B" }), getAtoms(new string[] { "d","foo","bar" }))
                               };
            string error;
            XAssert.IsTrue(DirectoryTranslator.ValidateDirectoryTranslation(pathTable, translations, out error));
        }

        [Fact]
        public void TestDirectoryTranslatorNoCycle2()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            DirectoryTranslator.RawInputTranslation[] translations = new[]
                                {
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "d", "foo", "bar" }), getAtoms(new string[] { "E" })),
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "A" }), getAtoms(new string[] { "B" })),
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "C" }), getAtoms(new string[] { "D" })),
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "B" }), getAtoms(new string[] { "C" }))
                               };
            string error;
            XAssert.IsTrue(DirectoryTranslator.ValidateDirectoryTranslation(pathTable, translations, out error));
        }

        [Fact]
        public void TestInvalidDirectoryTranslatorDueToCycle()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            DirectoryTranslator.RawInputTranslation[] translations = new[]
                                 {
                                   CreateInputTranslation(pathTable, getAtoms(new string[] {"d","foo","bar"}), getAtoms(new string[] {"E" })),
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "A" }), getAtoms(new string[] { "B" })),
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "C" }), getAtoms(new string[] { "A" })),
                                   CreateInputTranslation(pathTable, getAtoms(new string[] { "B" }), getAtoms(new string[] { "C" }))
                                 };
                string error;
                XAssert.IsFalse(DirectoryTranslator.ValidateDirectoryTranslation(pathTable, translations, out error));
                XAssert.AreEqual(@"cycle in directory translations '" + A(getAtoms(new string[] { "A" })) +
                        "' < '" + A(getAtoms(new string[] { "B" })) +
                        "' < '" + A(getAtoms(new string[] { "C" })) +
                        "' < '" + A(getAtoms(new string[] { "A" })) + "'", error);

        }

        [Fact]
        public void TestDirectoryTranslatorUsedForJunctionsInCloudBuild()
        {
            var context = BuildXLContext.CreateInstanceForTesting();
            var pathTable = context.PathTable;

            var translations = new[]
                               {
                                   CreateInputTranslation(pathTable, new string[] { "K","dbs","sh","dtb","b" }, new string[] { "d","dbs","sh","dtb","0629_120346" }),
                                   CreateInputTranslation(pathTable, new string[] { "d", "dbs","sh","dtb","0629_120346","Build" }, new string[] { "d", "dbs","el","dtb","Build" }),
                                   CreateInputTranslation(pathTable, new string[] { "d","dbs","sh","dtb","0629_120346","Target" }, new string[] { "d", "dbs","el","dtb","Target" })
                               };

            string error;
            XAssert.IsTrue(DirectoryTranslator.ValidateDirectoryTranslation(pathTable, translations, out error));

            var translator = new DirectoryTranslator();
            translator.AddTranslations(translations, pathTable);
            translator.Seal();

            AssertEqualTranslatedPath(translator, pathTable, new string[] { "d", "dbs", "el", "dtb", "Build", "x64", "debug", "perl.cmd" }, new string[] { "K", "dbs", "sh", "dtb", "b", "Build", "x64", "debug", "perl.cmd" });
            AssertEqualTranslatedPath(translator, pathTable, new string[] { "d", "dbs", "el", "dtb", "Target", "x64", "debug", "perl.cmd" }, new string[] { "K", "dbs", "sh", "dtb", "b", "Target", "x64", "debug", "perl.cmd" });
            AssertEqualTranslatedPath(translator, pathTable, new string[] { "d", "dbs", "sh", "dtb", "0629_120346" }, new string[] { "K", "dbs", "sh", "dtb", "b" });
            AssertEqualTranslatedPath(translator, new string[] { @"\\?\d", "dbs", "el", "dtb", "Build", "x64", "debug", "perl.cmd" }, new string[] { @"\\?\K", "dbs", "sh", "dtb", "b", "Build", "x64", "debug", "perl.cmd" });
            AssertEqualTranslatedPath(translator, new string[] { @"\??\d", "dbs", "el", "dtb", "Build", "x64", "debug", "perl.cmd" }, new string[] { @"\??\K", "dbs", "sh", "dtb", "b", "Build", "x64", "debug", "perl.cmd" });
        }

        private static void AssertEqualTranslatedPath(DirectoryTranslator translator, PathTable pathTable, string[] expected, string[] path)
        {
            string expectedAbsolute = A(expected);
            string pathAbsolute = A(path);

            XAssert.AreEqual(AbsolutePath.Create(pathTable, expectedAbsolute), translator.Translate(AbsolutePath.Create(pathTable, pathAbsolute), pathTable));
        }

        private static void AssertEqualTranslatedPath(DirectoryTranslator translator, string[] expected, string[] path)
        {
            string expectedAbsolute = A(expected);
            string pathAbsolute = A(path);

            XAssert.AreEqual(expectedAbsolute.ToUpperInvariant(), translator.Translate(pathAbsolute).ToUpperInvariant());
        }

        private static DirectoryTranslator.RawInputTranslation CreateInputTranslation(PathTable pathTable, string[] source, string[] target)
        {
            string sourceAbsolute = A(source);
            string targetAbsolute = A(target);

            return DirectoryTranslator.RawInputTranslation.Create(AbsolutePath.Create(pathTable, sourceAbsolute), AbsolutePath.Create(pathTable, targetAbsolute));
        }
    }
}

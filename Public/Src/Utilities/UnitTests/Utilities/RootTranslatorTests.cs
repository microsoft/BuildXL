// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using System.Collections.Generic;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class RootTranslatorTests : XunitBuildXLTest
    {
        private readonly RootTranslator m_translator = new RootTranslator();

        public RootTranslatorTests(ITestOutputHelper output)
            : base(output)
        {
        }

        public string ToAbsolutePath(string[] atoms){
            List<string> list = new List<string>(atoms);
            if(OperatingSystemHelper.IsUnixOS)
            {
                return A(null, list.ToArray());
            }
                return A(list.ToArray());
        }

        [Fact]
        public void TestRootTranslator()
        {
                AddTranslation(ToAbsolutePath(new string[]{ "D" }), ToAbsolutePath(new string[]{ "X", "underD", "" }));
                AddTranslation(ToAbsolutePath(new string[]{ "D", "root2" }), ToAbsolutePath(new string[]{"J", "R3" }));
                AddTranslation(ToAbsolutePath(new string[]{ "C", "test", "folder0" }), ToAbsolutePath(new string[]{ "M", "" }));
                AddTranslation(ToAbsolutePath(new string[]{ "F", "prefix" }), ToAbsolutePath(new string[]{ "N", "" }));
                AddTranslation(ToAbsolutePath(new string[]{ "K", "root2", "subroot", "phase2", "folder" }), ToAbsolutePath(new string[]{ "B", "" }));
                AddTranslation(ToAbsolutePath(new string[]{ "D", "root1" }), ToAbsolutePath(new string[]{ "K", "root2", "subroot" }));
                m_translator.Seal();

                // Generic translation
                TestRootTranslation(ToAbsolutePath(new string[] { "D", "fromRoot", "root.SLN" }), ToAbsolutePath(new string[]{"X", "underD", "fromRoot", "root.SLN" }));

                // Test NOT translated
                TestNotTranslated(ToAbsolutePath(new string[]{ "F", "prefix folder", "file.bin" }));
                TestNotTranslated(ToAbsolutePath(new string[]{ "F", "prefixbefore", "file.bin" }));
                TestNotTranslated(ToAbsolutePath(new string[]{ "F", "pref", "file.bin" }));

                // More specific translation takes precedence
                TestRootTranslation(ToAbsolutePath(new string[]{ "D", "root1", "files.txt" }), ToAbsolutePath(new string[]{ "K", "root2", "subroot", "files.txt" }));
                TestRootTranslation(ToAbsolutePath(new string[]{ "D", "root2", "nested", "files.txt" }), ToAbsolutePath(new string[]{ "J", "R3", "nested", "files.txt" }));
                TestRootTranslation(ToAbsolutePath(new string[]{ "D", "root1", "files2.txt" }), ToAbsolutePath(new string[]{ "K", "root2", "subroot", "files2.txt" }));

                // Nested folder translation
                TestRootTranslation(ToAbsolutePath(new string[]{ "C", "test", "folder0", "nested", "files.txt" }), ToAbsolutePath(new string[]{ "M", "nested", "files.txt" }));

                // Test that have chains of translations are translated appropriately
                // 1. D:\root1\phase2\folder\test\BLAH.blob -> K:\root2\subroot\phase2\folder\test\BLAH.blob
                // 2. K:\root2\subroot\phase2\folder\test\BLAH.blob -> B:\test\BLAH.blob
                TestRootTranslation(ToAbsolutePath(new string[]{ "D", "root1", "phase2", "folder", "test", "BLAH.blob" }), ToAbsolutePath(new string[]{ "B", "test", "BLAH.blob" }));
        }

        private void AddTranslation(string sourcePath, string targetPath)
        {
            m_translator.AddTranslation(sourcePath, targetPath);
        }

        private void TestNotTranslated(string path)
        {
            TestRootTranslation(path, path);
        }

        private void TestRootTranslation(string path, string expectedPath)
        {
            var translatedPath = m_translator.Translate(path);
            Assert.Equal(expectedPath, translatedPath);
        }
    }
}

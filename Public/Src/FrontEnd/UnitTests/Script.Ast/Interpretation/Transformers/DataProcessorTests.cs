// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Globalization;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation.Transformers
{
    /// <summary>
    /// Data processor tests.
    /// </summary>
    [Trait("Category", "Transformers")]
    public sealed class DataProcessorTests : DsTest
    {
        /// <nodoc/>
        public DataProcessorTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void TestSimple()
        {
            string Spec = String.Format(@"
const testString = Debug.dumpData('aString');
const testNumber = Debug.dumpData(123);
const testPath = Debug.dumpData(p`{0}path/to/dump`);
const testPathAtom = Debug.dumpData(a`anAtom`);
const testRelativePath = Debug.dumpData(r`relative/to/something`);
", OperatingSystemHelper.IsUnixOS ? "/" : "d:/");

            var result = EvaluateExpressionsWithNoErrors(Spec, "testString", "testNumber", "testPath", "testPathAtom", "testRelativePath");
            Assert.Equal("aString", result["testString"]);
            Assert.Equal(123.ToString(CultureInfo.InvariantCulture), result["testNumber"]);

            Assert.Equal(GetPathString(String.Format("{0}path" + Path.DirectorySeparatorChar + "to" + Path.DirectorySeparatorChar + "dump",
                OperatingSystemHelper.IsUnixOS ? "/" : "d:/")), result["testPath"]);

            Assert.Equal(GetAtomString("anAtom"), result["testPathAtom"]);
            Assert.Equal(GetRelativePathString(@"relative" + Path.DirectorySeparatorChar + "to" + Path.DirectorySeparatorChar + "something"), result["testRelativePath"]);
        }

        [Fact]
        public void TestCompound()
        {
            string Spec = String.Format(@"
const testCompound = Debug.dumpData({{
    separator: Environment.newLine(),
    contents: [
        'aString',
        123,
        p`{0}path/to/dump`,
        {{
            separator: ' ',
            contents: ['happy', 'new year']
        }}
    ]
}});
", OperatingSystemHelper.IsUnixOS ? "/" : "d:/");

            var result = EvaluateExpressionWithNoErrors(Spec, "testCompound");
            var expected = string.Join(
                Environment.NewLine,
                "aString",
                123.ToString(CultureInfo.InvariantCulture),
                GetPathString(String.Format("{0}path" + Path.DirectorySeparatorChar + "to" + Path.DirectorySeparatorChar + "dump", OperatingSystemHelper.IsUnixOS ? "/" : "d:/")),
                "happy new year");
            Assert.Equal(expected, result);
        }

        private string GetPathString(string path)
        {
            return AbsolutePath.Create(PathTable, path).ToString(PathTable);
        }

        private string GetAtomString(string atom)
        {
            return PathAtom.Create(StringTable, atom).ToString(StringTable);
        }

        private string GetRelativePathString(string relative)
        {
            return RelativePath.Create(StringTable, relative).ToString(StringTable);
        }
    }
}

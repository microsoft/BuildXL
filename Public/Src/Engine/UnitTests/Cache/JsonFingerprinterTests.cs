// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using BuildXL.Engine.Cache;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;
using Xunit;
using BuildXL.Storage;
using System.Linq;

namespace Test.BuildXL.Engine.Cache
{
    /// <summary>
    /// Test coverage for <see cref="JsonFingerprinter"/> and <see cref="CoreJsonFingerprinter"/>.
    /// These tests verify that valid JSON was written, but do not try and enforce any schemas.
    /// The serializers throw exceptions for attempting to write anything that would create invalid JSON,
    /// so any test that does not throw an exception is successful.
    /// </summary>
    public class JsonFingerprinterTests : XunitBuildXLTest
    {
        public StringBuilder StringBuilder { get; set; }

        public PathTable PathTable { get; set; }

        public JsonFingerprinterTests(ITestOutputHelper output) : base(output)
        {
            StringBuilder = new StringBuilder();
            PathTable = new PathTable();
        }

        private void VerifyJsonContent(params string[] expectedStrings)
        {
            // NewtonSoft adds extra "\", so remove them
            StringBuilder.Replace(@"\\", @"\");
            foreach (var str in expectedStrings)
            {
                XAssert.IsTrue(StringBuilder.ToString().Contains(str));
            }
        }

        [Fact]
        public void EmptyTest()
        {
            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
            }
        }

        [Fact]
        public void AddAbsolutePath()
        {
            AbsolutePath.TryCreate(PathTable, X("/d/nonsense"), out AbsolutePath path);

            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add("AbsolutePath", path);
            }
            VerifyJsonContent("AbsolutePath", PathToString(path));
        }
        
        [Fact]
        public void AddStringId()
        {
            var stringId = StringId.Create(PathTable.StringTable, "testString");

            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add("StringId", stringId);
            }

            VerifyJsonContent("StringId", stringId.ToString(PathTable.StringTable));
        }

        [Fact]
        public void AddContentHash()
        {
            AbsolutePath.TryCreate(PathTable, X("/d/nonsensePath"), out AbsolutePath path);
            var hash = ContentHashingUtilities.CreateRandom();

            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add(path, hash);
            }
            VerifyJsonContent(PathToString(path), JsonFingerprinter.ContentHashToString(hash));

            StringBuilder.Clear();
            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add("PathContentHash", path, hash);
            }
            VerifyJsonContent("PathContentHash", PathToString(path), JsonFingerprinter.ContentHashToString(hash));

            StringBuilder.Clear();
            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add("ContentHash", hash);
            }
            VerifyJsonContent("ContentHash", JsonFingerprinter.ContentHashToString(hash));
        }

        [Fact]
        public void AddCollection()
        {
            var intCollection = new int[] { 1, 2, 3 };
            var stringCollection = new string[] { X("/d/1"), X("/d/2"), X("/d/3") };

            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.AddCollection<int, int[]>("ints", intCollection, (w, e) => w.Add(e));
            }

            VerifyJsonContent(intCollection.Select(i => i.ToString()).ToArray());

            // Test multiple operations per element
            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.AddCollection<string, string[]>(
                    "strings", 
                    stringCollection, 
                    (w, e) =>
                    {
                        AbsolutePath.TryCreate(PathTable, e, out AbsolutePath path);
                        w.Add(e, path);
                        w.Add(path);
                    });
            }

            VerifyJsonContent(stringCollection);
        }

        [Fact]
        public void AddFingerprint()
        {
            var fingerprint = FingerprintUtilities.ZeroFingerprint;

            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add("fingerprint", fingerprint);
            }

            VerifyJsonContent("fingerprint", fingerprint.ToString());
        }

        [Fact]
        public void AddPrimitives()
        {
            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add("string", "text");
            }
            VerifyJsonContent("string", "text");

            StringBuilder.Clear();
            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add("int", 2323);
            }
            VerifyJsonContent("int", 2323.ToString());

            StringBuilder.Clear();
            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add("long", (long)235);
            }
            VerifyJsonContent("long", ((long)235).ToString());
        }

        [Fact]
        public void AddComboCollection()
        {
            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.AddCollection<int, int[]>("testCollection", new int[] { 1 }, (f, v) =>
                {
                    f.Add("string", "text");
                    f.Add(v);
                    f.Add("int", 2323);
                    f.Add("long", (long)2323);
                    f.Add(v);
                });
            }

            VerifyJsonContent("string", "text");
            VerifyJsonContent("int", 2323.ToString());
            VerifyJsonContent(1.ToString());
        }

        [Fact]
        public void ComboTest()
        {
            AbsolutePath.TryCreate(PathTable, X("/d/nonsensePath"), out AbsolutePath path);
            var stringId = StringId.Create(PathTable.StringTable, "testString");
            var fingerprint = FingerprintUtilities.ZeroFingerprint;
            var hash = ContentHashingUtilities.CreateRandom();
            var longValue = (long)23;
            var stringValue = "asdf";
            var intCollection = new int[] { 1, 2, 3 };

            // Do some random operations and make sure there's no exceptions
            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.Add(stringValue, longValue);
                writer.AddCollection<int, int[]>("ints", intCollection, (w, e) => w.Add(e));
                writer.Add(stringValue, hash);
                writer.Add(stringValue, stringId);
                writer.Add(stringValue, stringId);
                writer.Add(stringValue, path, hash);
            }

            VerifyJsonContent("testString");
            VerifyJsonContent(longValue.ToString());
            VerifyJsonContent(intCollection.Select(i => i.ToString()).ToArray());
            VerifyJsonContent(JsonFingerprinter.ContentHashToString(hash));
        }

        [Fact]
        public void NestedTest()
        {
            var intCollection = new int[] { 1, 2, 3 };
            var stringCollection = new string[] { X("/d/1"), X("/d/2"), X("/d/3") };

            string hello = "hello", world = "world", exclamation = "!";

            using (var writer = new JsonFingerprinter(StringBuilder, pathTable: PathTable))
            {
                writer.AddCollection<string, string[]>("strings", stringCollection, (fCollection, ele) =>
                {
                    fCollection.Add(ele);
                    fCollection.AddNested("nestedObject", (fNested) =>
                    {
                        fNested.AddCollection<int, int[]>("nestedCollectionInNestedObject", intCollection, (fNestedCollectionNestedObject, intEle) =>
                        {
                            fNestedCollectionNestedObject.Add(intEle);
                            fNestedCollectionNestedObject.Add(hello);
                        });
                    });
                    fCollection.AddCollection<int, int[]>("nestedCollection", intCollection, (fNestedCollection, intEle) =>
                    {
                        fNestedCollection.Add(intEle);
                        fNestedCollection.Add(exclamation);
                    });
                });

                writer.AddNested("object", (fNested) =>
                {
                    fNested.AddCollection<string, string[]>("strings", stringCollection, (fCollection, ele) =>
                    {
                        fCollection.AddNested("string", (fNestedInCollection) =>
                        {
                            fNestedInCollection.Add(ele, ele);
                            fNestedInCollection.Add(world, world);
                        });
                    });
                });
            }

            VerifyJsonContent(hello, world, exclamation);
        }

        /// <summary>
        /// Match <see cref="JsonFingerprinter.PathToString"/>.
        /// </summary>
        private string PathToString(AbsolutePath path)
        {
            // Normalize string paths to lower case since absolute path equivalency
            // depends on the hash and the path, but not casing
            return path.ToString(PathTable).ToLowerInvariant();
        }
    }
}

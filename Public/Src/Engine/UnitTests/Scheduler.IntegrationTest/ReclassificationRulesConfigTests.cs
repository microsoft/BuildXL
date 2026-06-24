// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Pips.Reclassification;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <summary>
    /// Test the construction of the reclassification rules based on the provided configuration.
    /// </summary>
    public class ReclassificationRulesConfigTests : TemporaryStorageTestBase
    {
        private static DiscriminatingUnion<ObservationType, UnitValue> Rt(ObservationType? observationType)
        {
            if (observationType == null)
            {
                return new DiscriminatingUnion<ObservationType, UnitValue>(UnitValue.Unit);
            }

            return new DiscriminatingUnion<ObservationType, UnitValue>(observationType.Value);
        }

        private readonly PathTable m_pathTable = new PathTable();

        public ReclassificationRulesConfigTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void BasicConstructionTests(bool varyCasing)
        {
            using ObservationReclassifier reclassificationRules = new ObservationReclassifier();
            reclassificationRules.Initialize([
                new DScriptInternalReclassificationRule(0, new ReclassificationRule ()
                {
                    PathRegex = "DIR[XZ]?.*",
                    Name = "MaybeGlobal",
                    ResolvedObservationTypes = [ ObservationType.ExistingDirectoryProbe ],
                    ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                }),
                new DScriptInternalReclassificationRule(1, new ReclassificationRule()
                {
                    PathRegex = "FILE",
                    Name = "TheseFileProbesAreAbsent",
                    ResolvedObservationTypes = [ ObservationType.ExistingFileProbe ],
                    ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                })
             ]);

            // Now, select only the second rule to apply
            var selectedRuleName = varyCasing ? "THESEFILEPROBESAREABSENT" : "TheseFileProbesAreAbsent";

            FingerprintReclassificationResult reclassification;
            var filePath = "/c/My/FILEMatchesTheSecondRule";
            if (varyCasing && !OperatingSystemHelper.IsPathComparisonCaseSensitive)
            {
                // Regex should be case insensitive in this OS
                filePath = filePath.ToLower();
            }
            var someMatchingPath = AbsolutePath.Create(m_pathTable, X(filePath));
            XAssert.IsTrue(reclassificationRules.TryReclassify(someMatchingPath, m_pathTable, ObservedInputType.ExistingFileProbe, out reclassification));
            XAssert.AreEqual(ObservedInputType.AbsentPathProbe, reclassification.ReclassifyToType);
            XAssert.AreEqual("TheseFileProbesAreAbsent", reclassification.AppliedRuleName);
            
            // Don't reclassify other types 
            XAssert.IsFalse(reclassificationRules.TryReclassify(someMatchingPath, m_pathTable, ObservedInputType.ExistingDirectoryProbe, out _));

            // Don't match other paths
            var nonMatchingPath = AbsolutePath.Create(m_pathTable, X("/c/My/WontMatchTheSecondRule"));
            XAssert.IsFalse(reclassificationRules.TryReclassify(nonMatchingPath, m_pathTable, ObservedInputType.ExistingFileProbe, out _));
        }

        [Fact]
        public void YarnStrictRuleBehavior()
        {
            using ObservationReclassifier reclassificationRules = new ObservationReclassifier();
            
            var storeRoot = AbsolutePath.Create(m_pathTable, TemporaryDirectory).Combine(m_pathTable, ".store");
            // Create two fake packages in the store
            var testPackageRoot = storeRoot.Combine(m_pathTable, "test-module@1.0.0");
            var testPackageContent = testPackageRoot.Combine(m_pathTable, "index.js");
            Directory.CreateDirectory(testPackageRoot.ToString(m_pathTable));
            File.WriteAllText(testPackageContent.ToString(m_pathTable), "console.log('Hello, world!');");

            var testPackageRoot2 = storeRoot.Combine(m_pathTable, "another-test-module@1.0.1");
            var testPackageContent2 = testPackageRoot2.Combine(m_pathTable, "index.js");
            Directory.CreateDirectory(testPackageRoot2.ToString(m_pathTable));
            File.WriteAllText(testPackageContent2.ToString(m_pathTable), "console.log('Hello, world!');");

            reclassificationRules.Initialize([
                new JavaScriptPackageStoreReclassificationRule("test-module", storeRoot)
             ]);

            FingerprintReclassificationResult reclassification;

            // A probe to the content of a package under the store should be reclassified as a probe on the directory of the package
            XAssert.IsTrue(reclassificationRules.TryReclassify(testPackageContent, m_pathTable, ObservedInputType.ExistingFileProbe, out reclassification));
            XAssert.AreEqual(ObservedInputType.ExistingDirectoryProbe, reclassification.ReclassifyToType);
            XAssert.AreEqual(testPackageRoot, reclassification.ReclassifyToPath);

            // Absent probes under the store are ignored (reclassified with null type), regardless of package existence
            var packageAccessAbsent = testPackageRoot2.Combine(m_pathTable, "absent-file");
            XAssert.IsTrue(reclassificationRules.TryReclassify(packageAccessAbsent, m_pathTable, ObservedInputType.AbsentPathProbe, out reclassification));
            XAssert.AreEqual(null, reclassification.ReclassifyToType);

            // Directory enumerations under the store are also ignored
            XAssert.IsTrue(reclassificationRules.TryReclassify(testPackageContent2, m_pathTable, ObservedInputType.DirectoryEnumeration, out reclassification));
            XAssert.AreEqual(null, reclassification.ReclassifyToType);

            // A read on the second package should still be reclassified to a probe on the package root (absent probes/enumerations don't consume the first-access slot)
            XAssert.IsTrue(reclassificationRules.TryReclassify(testPackageContent2, m_pathTable, ObservedInputType.ExistingFileProbe, out reclassification));
            XAssert.AreEqual(ObservedInputType.ExistingDirectoryProbe, reclassification.ReclassifyToType);
            XAssert.AreEqual(testPackageRoot2, reclassification.ReclassifyToPath);

            // Any subsequent read to the same package should be reclassified to ignore the access
            XAssert.IsTrue(reclassificationRules.TryReclassify(testPackageContent, m_pathTable, ObservedInputType.ExistingFileProbe, out reclassification));
            XAssert.AreEqual(null, reclassification.ReclassifyToType);
            XAssert.IsTrue(reclassificationRules.TryReclassify(testPackageContent2, m_pathTable, ObservedInputType.ExistingFileProbe, out reclassification));
            XAssert.AreEqual(null, reclassification.ReclassifyToType);

            // Accesses to paths outside the store should not be reclassified
            var nonMatchingPath = AbsolutePath.Create(m_pathTable, TemporaryDirectory).Combine(m_pathTable, "out-of-store");
            XAssert.IsFalse(reclassificationRules.TryReclassify(nonMatchingPath, m_pathTable, ObservedInputType.AbsentPathProbe, out _));

            // Absent probes to non-existing packages under the store are also ignored
            var nonExistingPackageRoot = storeRoot.Combine(m_pathTable, "non-existing-package@1.0.0");
            var nonExistingPackagePath = nonExistingPackageRoot.Combine(m_pathTable, "some-file.js");

            XAssert.IsTrue(reclassificationRules.TryReclassify(nonExistingPackagePath, m_pathTable, ObservedInputType.AbsentPathProbe, out reclassification));
            XAssert.AreEqual(null, reclassification.ReclassifyToType);
        }

        /// <summary>
        /// The dedup of accesses to the same package (only the first access produces a probe, the rest are ignored) must be scoped to a single pip.
        /// In production, a single rule instance is shared across pips but each pip gets its own <see cref="ObservationReclassifier"/> (see ObservedInputProcessor).
        /// This test simulates two pips that share the same observation under the package store and verifies that the dedup state does not leak across pips:
        /// both pips must reclassify the shared access to a probe on the package directory (otherwise the result would depend on the order in which pips execute).
        /// </summary>
        [Fact]
        public void YarnStrictRuleDoesNotCacheAcrossPips()
        {
            var storeRoot = AbsolutePath.Create(m_pathTable, TemporaryDirectory).Combine(m_pathTable, ".store");
            var testPackageRoot = storeRoot.Combine(m_pathTable, "test-module@1.0.0");
            var testPackageContent = testPackageRoot.Combine(m_pathTable, "index.js");
            Directory.CreateDirectory(testPackageRoot.ToString(m_pathTable));
            File.WriteAllText(testPackageContent.ToString(m_pathTable), "console.log('Hello, world!');");

            // A single rule instance
            var sharedRule = new JavaScriptPackageStoreReclassificationRule("test-module", storeRoot);

            // Each pip gets its own ObservationReclassifier, but both are initialized with the same shared rule instance
            using var reclassifierForPipA = new ObservationReclassifier();
            reclassifierForPipA.Initialize([sharedRule]);

            using var reclassifierForPipB = new ObservationReclassifier();
            reclassifierForPipB.Initialize([sharedRule]);

            // Pip A reads content under the package: it should be reclassified to a probe on the package directory
            XAssert.IsTrue(reclassifierForPipA.TryReclassify(testPackageContent, m_pathTable, ObservedInputType.ExistingFileProbe, out var reclassificationA));
            XAssert.AreEqual(ObservedInputType.ExistingDirectoryProbe, reclassificationA.ReclassifyToType);
            XAssert.AreEqual(testPackageRoot, reclassificationA.ReclassifyToPath);

            // Pip B reads the exact same content under the same package. Because the dedup state is per-pip (not held in the shared rule),
            // pip B must also get the access reclassified to a probe on the package directory, not ignored.
            XAssert.IsTrue(reclassifierForPipB.TryReclassify(testPackageContent, m_pathTable, ObservedInputType.ExistingFileProbe, out var reclassificationB));
            XAssert.AreEqual(ObservedInputType.ExistingDirectoryProbe, reclassificationB.ReclassifyToType);
            XAssert.AreEqual(testPackageRoot, reclassificationB.ReclassifyToPath);

            // Sanity check: within a single pip, a subsequent access to the same package is still deduplicated (ignored)
            XAssert.IsTrue(reclassifierForPipA.TryReclassify(testPackageContent, m_pathTable, ObservedInputType.ExistingFileProbe, out var reclassificationADuplicate));
            XAssert.AreEqual(null, reclassificationADuplicate.ReclassifyToType);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DuplicateNameIsAnError(bool casing)
        {
            using var rules = new ObservationReclassifier();
            Assert.Throws<BuildXLException>(() => rules.Initialize([
                        new DScriptInternalReclassificationRule(0, new ReclassificationRule()
                        {
                            PathRegex = "DIR",
                            Name = "MyRuleName",
                            ResolvedObservationTypes = [ ObservationType.ExistingDirectoryProbe ],
                            ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                        }),
                        new DScriptInternalReclassificationRule(1, new ReclassificationRule()
                        {
                            PathRegex = "FILE",
                            Name = casing ? "MYRULENAME" : "MyRuleName",
                            ResolvedObservationTypes = [ ObservationType.ExistingFileProbe ],
                            ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                        })
                    ]));
        }

        [Theory]
        [InlineData(true, ObservationType.ExistingFileProbe, ObservationType.FileContentRead)]
        [InlineData(false, ObservationType.DirectoryEnumeration, ObservationType.FileContentRead)]
        [InlineData(true, ObservationType.ExistingDirectoryProbe, ObservationType.DirectoryEnumeration)]
        [InlineData(false, ObservationType.FileContentRead, ObservationType.DirectoryEnumeration)]
        public void InvalidTranslationTest(bool expectValid, ObservationType from, ObservationType to)
        {
            using var rules = new ObservationReclassifier();
            var reclassificationRule = new DScriptInternalReclassificationRule(0, new ReclassificationRule()
            {
                PathRegex = "DIR",
                Name = "MyRuleName",
                ResolvedObservationTypes = [from],
                ReclassifyTo = Rt(to)
            });

            if (expectValid)
            {
                rules.Initialize([ reclassificationRule ]);
            }
            else
            {
                Assert.Throws<BuildXLException>(() => rules.Initialize([reclassificationRule]));
            }
        }

        [Fact]
        public void Serialization()
        {
            using ObservationReclassifier rules = new ObservationReclassifier();
            rules.Initialize([
                new DScriptInternalReclassificationRule(0, new ReclassificationRule()
                {
                    PathRegex = "FILE",
                    Name = "TheseFileProbesAreAbsent",
                    ResolvedObservationTypes = [ ObservationType.ExistingFileProbe, ObservationType.ExistingDirectoryProbe ],
                    ReclassifyTo = Rt(ObservationType.ExistingFileProbe)
                }),
                new DScriptInternalReclassificationRule(1, new ReclassificationRule()
                {
                    PathRegex = "DIR",
                    Name = "MaybeGlobal",
                    ResolvedObservationTypes = [ ObservationType.ExistingDirectoryProbe ],
                    ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                })
            ]);

            var ms = new MemoryStream();
            BuildXLWriter writer = new BuildXLWriter(true, ms, true, true);
            rules.Serialize(writer);

            ms.Position = 0;
            BuildXLReader reader = new BuildXLReader(true, ms, true);
            using var deserialized = ObservationReclassifier.Deserialize(reader); 
        }
    }
}

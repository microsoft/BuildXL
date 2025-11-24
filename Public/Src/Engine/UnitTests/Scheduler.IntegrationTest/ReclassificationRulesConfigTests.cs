// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <summary>
    /// Test the construction of the reclassification rules based on the provided configuration.
    /// </summary>
    public class ReclassificationRulesConfigTests : XunitBuildXLTest
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
            ObservationReclassifier reclassificationRules = new ObservationReclassifier();
            reclassificationRules.Initialize([
                new ReclassificationRule ()
                {
                    PathRegex = "DIR[XZ]?.*",
                    Name = "MaybeGlobal",
                    ResolvedObservationTypes = [ ObservationType.ExistingDirectoryProbe ],
                    ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                },
                new ReclassificationRule()
                {
                    PathRegex = "FILE",
                    Name = "TheseFileProbesAreAbsent",
                    ResolvedObservationTypes = [ ObservationType.ExistingFileProbe ],
                    ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                }
             ]);

            // Now, select only the second rule to apply
            var selectedRuleName = varyCasing ? "THESEFILEPROBESAREABSENT" : "TheseFileProbesAreAbsent";

            ReclassificationResult reclassification;
            var filePath = "/c/My/FILEMatchesTheSecondRule";
            if (varyCasing && !OperatingSystemHelper.IsPathComparisonCaseSensitive)
            {
                // Regex should be case insensitive in this OS
                filePath = filePath.ToLower();
            }
            var someMatchingPath = AbsolutePath.Create(m_pathTable, X(filePath));
            XAssert.IsTrue(reclassificationRules.TryReclassify(someMatchingPath, m_pathTable, ObservedInputType.ExistingFileProbe, out reclassification));
            XAssert.AreEqual(ObservedInputType.AbsentPathProbe, reclassification.ReclassifyTo);
            XAssert.AreEqual("TheseFileProbesAreAbsent", reclassification.AppliedRuleName);

            // Don't reclassify other types 
            XAssert.IsFalse(reclassificationRules.TryReclassify(someMatchingPath, m_pathTable, ObservedInputType.ExistingDirectoryProbe, out _));

            // Don't match other paths
            var nonMatchingPath = AbsolutePath.Create(m_pathTable, X("/c/My/WontMatchTheSecondRule"));
            XAssert.IsFalse(reclassificationRules.TryReclassify(nonMatchingPath, m_pathTable, ObservedInputType.ExistingFileProbe, out _));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DuplicateNameIsAnError(bool casing)
        {
            var rules = new ObservationReclassifier();
            Assert.Throws<BuildXLException>(() => rules.Initialize([
                        new ReclassificationRule()
                        {
                            PathRegex = "DIR",
                            Name = "MyRuleName",
                            ResolvedObservationTypes = [ ObservationType.ExistingDirectoryProbe ],
                            ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                        },
                        new ReclassificationRule()
                        {
                            PathRegex = "FILE",
                            Name = casing ? "MYRULENAME" : "MyRuleName",
                            ResolvedObservationTypes = [ ObservationType.ExistingFileProbe ],
                            ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                        }
                    ]));
        }

        [Theory]
        [InlineData(true, ObservationType.ExistingFileProbe, ObservationType.FileContentRead)]
        [InlineData(false, ObservationType.DirectoryEnumeration, ObservationType.FileContentRead)]
        [InlineData(true, ObservationType.ExistingDirectoryProbe, ObservationType.DirectoryEnumeration)]
        [InlineData(false, ObservationType.FileContentRead, ObservationType.DirectoryEnumeration)]
        public void InvalidTranslationTest(bool expectValid, ObservationType from, ObservationType to)
        {
            var rules = new ObservationReclassifier();
            var reclassificationRule = new ReclassificationRule()
            {
                PathRegex = "DIR",
                Name = "MyRuleName",
                ResolvedObservationTypes = [from],
                ReclassifyTo = Rt(to)
            };

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
            ObservationReclassifier rules = new ObservationReclassifier();
            rules.Initialize([
                new ReclassificationRule()
                {
                    PathRegex = "FILE",
                    Name = "TheseFileProbesAreAbsent",
                    ResolvedObservationTypes = [ ObservationType.ExistingFileProbe, ObservationType.ExistingDirectoryProbe ],
                    ReclassifyTo = Rt(ObservationType.ExistingFileProbe)
                },
                new ReclassificationRule()
                {
                    PathRegex = "DIR",
                    Name = "MaybeGlobal",
                    ResolvedObservationTypes = [ ObservationType.ExistingDirectoryProbe ],
                    ReclassifyTo = Rt(ObservationType.AbsentPathProbe)
                }
            ]);

            var ms = new MemoryStream();
            BuildXLWriter writer = new BuildXLWriter(true, ms, true, true);
            rules.Serialize(writer);

            ms.Position = 0;
            BuildXLReader reader = new BuildXLReader(true, ms, true);
            var deserialized = ObservationReclassifier.Deserialize(reader); 
        }
    }
}

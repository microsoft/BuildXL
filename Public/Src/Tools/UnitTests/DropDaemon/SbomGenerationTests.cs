// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Tool.DropDaemon;
using Microsoft.ManifestGenerator;
using Microsoft.Sbom.Contracts;
using Microsoft.Sbom.Contracts.Enums;
using SBOMCore;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Sbom.Contracts.Entities;
using Microsoft.Sbom.Adapters;
using Microsoft.Sbom.Adapters.Report;
using System.Text;

namespace Test.Tool.DropDaemon
{
    public sealed class SbomGenerationTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public SbomGenerationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ExpectingFailuresOnFailedCgParsing(bool missingFile)
        {
            // Test for absent file in one case, for malformed JSON in the other
            var path = missingFile ? Path.GetTempFileName() : GenerateBcdeOutput(Path.GetTempFileName(), malformed: true);
            var (adapterReport, packages) = new ComponentDetectionToSBOMPackageAdapter().TryConvert(path);
            
            XAssert.IsNotNull(packages, "ComponentDetectionToSBOMPackageAdapter shouldn't return null on failure");

            // This will make us fail
            XAssert.IsTrue(adapterReport.Report.Any(r => r.Type == AdapterReportItemType.Failure), "Expecting a failure in the adapter report");
        }

        /// <summary>
        /// Sanity check to test regressions in the SBOM Generator. 
        /// We had a lot of issues with missing runtime dependencies while updating those libraries.
        /// By keeping the test module imports in sync with the dropDaemonSbomPackages() function
        /// we can make sure with this test that the generator will run properly.
        /// </summary>
        [Fact]
        public async Task GenerateSbom()
        {
            var sbomGenerator = new SBOMGenerator();

            var metadata = new SBOMMetadata()
            {
                BuildEnvironmentName = "BuildXL",
                BuildName = "chelo",
                BuildId = "chelo-chelo-chelo-chelo",
                CommitId = "deadc0ff33",
                Branch = "chelivery",
                RepositoryUri = "http://github.com/microsoft/BuildXL",
                PackageName = "Chelo Test",
                PackageVersion = "2.1.21"
            };

            var sbomGenerationRootDirectory = Path.Combine(Path.GetTempPath(), "sbom");
            
            var specs = new List<SbomSpecification>() { new("SPDX", "2.2") };
            var myfile = new SbomFile()
            {
                Id = "MyFileId",
                Path = "Oh/What/A/Cool/Path.txt",
                Checksum = new[] 
                {
                    new Checksum()
                    {
                        Algorithm = AlgorithmName.SHA1,
                        ChecksumValue = "PretendThisIsAnotherHash"
                    },
                    new Checksum()
                    {
                        Algorithm = AlgorithmName.SHA256,
                        ChecksumValue = "PleaseKeepPretendinTheseAreHashes"
                    },
                }
            };
            IEnumerable<SbomFile> files = new List<SbomFile>() { myfile };

            var (adapterReport, packages) = new ComponentDetectionToSBOMPackageAdapter().TryConvert(GenerateBcdeOutput(Path.GetTempFileName()));
            XAssert.IsNotNull(packages);
            foreach (var reportItem in adapterReport.Report)
            {
                if (reportItem.Type == AdapterReportItemType.Failure)
                {
                    XAssert.Fail("ComponentDetectionToSBOMPackageAdapter failure: " + reportItem.Details);
                }
            }

            var result = await sbomGenerator.GenerateSbomAsync(sbomGenerationRootDirectory, files, packages, metadata, specs);
            if (!result.IsSuccessful)
            {
                var errorDetails = GetSbomGenerationErrorDetails(result.Errors);
                XAssert.Fail($"Sbom generation failed with errors: {errorDetails}");
            }
            XAssert.IsTrue(result.IsSuccessful);
        }


        private static string GetSbomGenerationErrorDetails(IList<EntityError> errors)
        {
            var sb = new StringBuilder();
            foreach (var error in errors)
            {
                sb.AppendLine($"Error of type {error.ErrorType} for entity {(error.Entity as FileEntity)?.Path ?? error.Entity.Id} of type {error.Entity.EntityType}:");
                sb.AppendLine(error.Details);
            }

            return sb.ToString();
        }

        private static string GenerateBcdeOutput(string tempFilePath, bool malformed = false) 
        {
            // A minimized but complete version of a bcde-output grabbed from a real build
            string spec = (malformed ? "{" : "") + @"
{
  ""dependencyGraphs"": 
  {
    ""D:\\dbs\\el\\bxlint\\Shared\\Npm\\packages\\buildxl\\yarn.lock"": {
      ""graph"": {
        ""path-is-absolute 1.0.1 - Npm"": null,
        ""chalk 4.1.0 - Npm"": [
          ""ansi-styles 4.3.0 - Npm"",
        ]
      },
      ""explicitlyReferencedComponentIds"": [
        ""chalk 4.1.0 - Npm""
      ],
      ""developmentDependencies"": [
        ""asynckit 0.4.0 - Npm"",
      ],
      ""dependencies"": [
        ""path-is-absolute 1.0.1 - Npm"",
      ]
    }
  },
  ""componentsFound"": [
   {
      ""locationsFoundAt"": [
        ""/Shared/Npm/packages/buildxl/yarn.lock""
      ],
      ""component"": {
        ""name"": ""fs.realpath"",
        ""version"": ""1.0.0"",
        ""hash"": null,
        ""author"": null,
        ""type"": ""Npm"",
        ""id"": ""fs.realpath 1.0.0 - Npm"",
        ""packageUrl"": {
          ""Scheme"": ""pkg"",
          ""Type"": ""npm"",
          ""Namespace"": null,
          ""Name"": ""fs.realpath"",
          ""Version"": ""1.0.0"",
          ""Qualifiers"": null,
          ""Subpath"": null
        }
      },
      ""detectorId"": ""Yarn"",
      ""isDevelopmentDependency"": false,
      ""dependencyScope"": null,
      ""topLevelReferrers"": [
        {
          ""name"": ""rimraf"",
          ""version"": ""3.0.2"",
          ""hash"": null,
          ""author"": null,
          ""type"": ""Npm"",
          ""id"": ""rimraf 3.0.2 - Npm"",
          ""packageUrl"": {
            ""Scheme"": ""pkg"",
            ""Type"": ""npm"",
            ""Namespace"": null,
            ""Name"": ""rimraf"",
            ""Version"": ""3.0.2"",
            ""Qualifiers"": null,
            ""Subpath"": null
          }
        }
      ],
      ""containerDetailIds"": [],
      ""containerLayerIds"": {}
    }
  ],
  ""detectorsInScan"": [
    {
      ""detectorId"": ""Yarn"",
      ""isExperimental"": false,
      ""version"": 6,
      ""supportedComponentTypes"": [
        ""Npm""
      ]
    }
  ],
  ""containerDetailsMap"": {},
  ""resultCode"": ""Success""
}
";
            using(var writer = new StreamWriter(tempFilePath))
            {
                writer.Write(spec);
                writer.Flush();
            }
            
            return tempFilePath;
        }
    }
}

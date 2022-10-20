// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Tool.DropDaemon;
using Microsoft.ManifestGenerator;
using Microsoft.Sbom.Contracts;
using Microsoft.Sbom.Contracts.Enums;
using SBOMCore;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Sbom.Contracts.Entities;
using System.Text;

namespace Test.Tool.DropDaemon
{
    public sealed class SbomGenerationTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public SbomGenerationTests(ITestOutputHelper output)
            : base(output)
        {
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
            
            var specs = new List<SBOMSpecification>() { new("SPDX", "2.2"), new("CloudBuildManifest", "1.0.0") };

            // A file with VSO and SHA1 hashes to generate both SPDX and CBManifest
            var myfile = new SBOMFile()
            {
                Id = "MyFileId",
                Path = "Oh/What/A/Cool/Path.txt",
                Checksum = new[] 
                {
                    new Checksum()
                    {
                        Algorithm = CBHashAlgorithmName.VSO,
                        ChecksumValue = "PretendThisIsAHash"
                    },
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
            IEnumerable<SBOMFile> files = new List<SBOMFile>() { myfile };

            // A meagre package
            var mypkg = new SBOMPackage()
            {
                Id = "MyPackageId",
                PackageName = "MyPackageName"
            };

            IEnumerable<SBOMPackage> packages = new List<SBOMPackage>() { mypkg };

            var result = await sbomGenerator.GenerateSBOMAsync(sbomGenerationRootDirectory, files, packages, metadata, specs);
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
    }
}

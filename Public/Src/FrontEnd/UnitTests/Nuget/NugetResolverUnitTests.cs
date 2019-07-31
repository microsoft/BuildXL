// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Engine;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Nuget
{

    //TODO: Start using virtual file system.
    public class NugetResolverUnitTests : TemporaryStorageTestBase
    {
        private readonly FrontEndContext m_testContext;

        public NugetResolverUnitTests(ITestOutputHelper output)
            : base(output)
        {
            var pathTable = new PathTable();
            m_testContext = FrontEndContext.CreateInstanceForTesting(pathTable: pathTable, fileSystem: new PassThroughFileSystem(pathTable));
        }

        [Fact]
        public void LoadingContentsFromDownloadedPackage()
        {
            var packageOnDisk = CreateTestPackageOnDisk(includeScriptSpec: true);

            XAssert.IsTrue(packageOnDisk.ModuleConfigFile.IsValid);
            XAssert.IsTrue(packageOnDisk.NuSpecFile.IsValid);
            XAssert.AreEqual(6, packageOnDisk.Contents.Count);
        }

        [Fact]
        public void LoadingContentsFromDownloadedPackageMissingFiles()
        {
            var packageOnDisk = CreateTestPackageOnDisk(includeScriptSpec: false);

            XAssert.IsFalse(packageOnDisk.ModuleConfigFile.IsValid);
            XAssert.IsTrue(packageOnDisk.NuSpecFile.IsValid);
            XAssert.AreEqual(3, packageOnDisk.Contents.Count);
        }

        [Fact]
        public void TestGenerationIsNotTriggeredWhenEmbeddedSpecIsPresent()
        {
            var packageOnDisk = CreateTestPackageOnDisk(includeScriptSpec: true);

            var nugetResolver = CreateWorkspaceNugetResolverForTesting();

            var allPackages = new Dictionary<string, INugetPackage> { [packageOnDisk.Package.Id] = packageOnDisk.Package };
            var analyzedPackage = nugetResolver.AnalyzeNugetPackage(packageOnDisk, false);

            XAssert.IsTrue(analyzedPackage.Succeeded);

            var result =
                nugetResolver.GenerateSpecsForDownloadedPackages(
                    new Dictionary<string, NugetAnalyzedPackage> { [analyzedPackage.Result.Id] = analyzedPackage.Result });

            // We shouldn't generate anything when an embedded spec is present
            XAssert.AreEqual(0, result.Count);
        }

        [Fact]
        public void TestGetOrGenerateSpecWithoutEmbeddedSpec()
        {
            var pathTable = m_testContext.PathTable;

            var packageOnDisk = CreateTestPackageOnDisk(includeScriptSpec: false);
            var nugetResolver = CreateWorkspaceNugetResolverForTesting();

            var allPackages = new Dictionary<string, INugetPackage> { [packageOnDisk.Package.Id] = packageOnDisk.Package };

            var analyzedPackage = nugetResolver.AnalyzeNugetPackage(packageOnDisk, false);

            XAssert.IsTrue(analyzedPackage.Succeeded);

            var result = nugetResolver.GenerateSpecFile(analyzedPackage.Result);

            XAssert.IsTrue(result.Succeeded);
            XAssert.IsTrue(result.Result.IsValid);
            var generatedContent = result.Result.GetParent(pathTable).ToString(pathTable);
            var expectedGeneratedSpecCount = 2;
            var expectedGeneratedMetataFileCount = 1;
            XAssert.AreEqual(expectedGeneratedSpecCount + expectedGeneratedMetataFileCount, Directory.EnumerateFiles(generatedContent, "*.*", SearchOption.AllDirectories).Count());

            // package file is tested already
            var packageDsc = Path.Combine(generatedContent, "package.dsc");
            XAssert.IsTrue(File.Exists(packageDsc));
            var packageText = File.ReadAllText(packageDsc);
            XAssert.IsFalse(packageText.Contains("export * from"));
            XAssert.IsFalse(packageText.Contains("/Foo.Bar.dsc\""));
            XAssert.IsTrue(packageText.Contains("/pkgs/Foo.Bar.1.2`"));
            XAssert.IsTrue(packageText.Contains("packageRoot = d`"));
            XAssert.IsTrue(packageText.Contains("f`${packageRoot}/Folder/a.txt`"));
            XAssert.IsTrue(packageText.Contains("f`${packageRoot}/Folder/b.txt`"));
        }

        [Fact]
        public void TestSpecGeneratorIncrementality()
        {
            // make sure we can generate a package
            var specOutput = GenerateSpecAndValidateExistence("1.2");

            // Delete the file and make sure that it is correctly recreated
            File.Delete(specOutput);
            specOutput = GenerateSpecAndValidateExistence("1.2");

            // Scribble some marker at the end of the file. Use this to make sure the file isn't regenerated if
            // nothing else changes
            const string DummyMarker = "DummyMarker";
            File.AppendAllText(specOutput, DummyMarker);
            specOutput = GenerateSpecAndValidateExistence("1.2");
            XAssert.IsTrue(File.ReadAllText(specOutput).Contains(DummyMarker));

            // Change the version of the package (which flows into the fingerprint). This should cause the package
            // to get regenerated
            specOutput = GenerateSpecAndValidateExistence("1.3");
            XAssert.IsFalse(File.ReadAllText(specOutput).Contains(DummyMarker));
        }

        private string GenerateSpecAndValidateExistence(string version)
        {
            // Setup state
            var pathTable = m_testContext.PathTable;
            var packageOnDisk = CreateTestPackageOnDisk(includeScriptSpec: false, version: version);
            var nugetResolver = CreateWorkspaceNugetResolverForTesting();
            var analyzedPackage = nugetResolver.AnalyzeNugetPackage(packageOnDisk, false);
            XAssert.IsTrue(analyzedPackage.Succeeded);
            var allPackages = new Dictionary<string, NugetAnalyzedPackage> { [packageOnDisk.Package.Id] = analyzedPackage.Result };
            XAssert.IsTrue(analyzedPackage.Succeeded);

            // Generate a spec. We just look at the first result since there's only one spec
            var results = nugetResolver.GenerateSpecsForDownloadedPackages(allPackages);
            var result = results.FirstOrDefault().Value;
            XAssert.IsTrue(result.Succeeded);
            var path = result.Result.ToString(pathTable);
            XAssert.IsTrue(File.Exists(path));
            return path;
        }

        [Fact]
        public void TestEmbeddedSpecsAreInterpreted()
        {
            var nugetResolver = CreateWorkspaceNugetResolverForTesting();

            var packageWithEmbeddedSpecs = CreateTestPackageOnDisk(includeScriptSpec: true);

            var maybeDescriptors = nugetResolver.ConfigureWithPackages(packageWithEmbeddedSpecs).GetAllKnownModuleDescriptorsAsync().GetAwaiter().GetResult();
            XAssert.IsTrue(maybeDescriptors.Succeeded);

            XAssert.AreEqual(1, maybeDescriptors.Result.Count);

            var descriptor = maybeDescriptors.Result.First();

            var maybeDefinition = nugetResolver.TryGetModuleDefinitionAsync(descriptor).GetAwaiter().GetResult();
            XAssert.IsTrue(maybeDefinition.Succeeded);
            XAssert.AreEqual(2, maybeDefinition.Result.Specs.Count);
        }

        [Fact]
        public void TestGeneratedSpecsAreInterpreted()
        {
            var nugetResolver = CreateWorkspaceNugetResolverForTesting();

            var package = CreateTestPackageOnDisk(includeScriptSpec: false);

            var maybeDescriptors = nugetResolver.ConfigureWithPackages(package).GetAllKnownModuleDescriptorsAsync().GetAwaiter().GetResult();
            XAssert.IsTrue(maybeDescriptors.Succeeded);

            XAssert.AreEqual(1, maybeDescriptors.Result.Count);
        }

        [Fact]
        public void TestBothGeneratedAndEmbeddedSpecsAreInterpreted()
        {
            var nugetResolver = CreateWorkspaceNugetResolverForTesting();

            var package = CreateTestPackageOnDisk(includeScriptSpec: false, packageName: "A");
            var packageWithEmbeddedSpecs = CreateTestPackageOnDisk(includeScriptSpec: true, packageName: "B");

            var maybeDescriptors = nugetResolver.ConfigureWithPackages(package, packageWithEmbeddedSpecs).GetAllKnownModuleDescriptorsAsync().GetAwaiter().GetResult();
            XAssert.IsTrue(maybeDescriptors.Succeeded);

            XAssert.AreEqual(2, maybeDescriptors.Result.Count);
        }

        [Theory]
#if PLATFORM_WIN
        [InlineData("http2://asd.sdfs.com", FailureType.InvalidUri)]
        [InlineData("1http://asd.sdfs.com", FailureType.InvalidUri)]
#else
        [InlineData("http2://asd.sdfs.com", FailureType.CopyFile)]
        [InlineData("1http://asd.sdfs.com", FailureType.CopyFile)]
#endif
        [InlineData("http://asd.sdweribfs.cdsfom", FailureType.Download)]
        [InlineData("https://asd.sdweribfs.cdsfom", FailureType.Download)]
        [InlineData("file://L/a/b/c.txt", FailureType.CopyFile)]
        [InlineData("L:\\a\\b\\c.txt", FailureType.CopyFile)]
        [InlineData("a\\b\\c.txt", FailureType.CopyFile)]
        public async Task TestDownloadFileFailures(string url, FailureType failureType)
        {
            var host = CreateFrontEndHostControllerForTesting();
            var targetPath = Path.Combine(TemporaryDirectory, nameof(TestDownloadFileFailures), "target.pkg");
            var maybeResult = await host.DownloadFile(
                host.FrontEndContext.LoggingContext,
                friendlyName: "test-package",
                url: url,
                targetFilePath: targetPath);
            XAssert.IsFalse(maybeResult.Succeeded);
            XAssert.AreEqual(typeof(FileDownloadFailure), maybeResult.Failure.GetType(), "Unexpected failure type");
            //XAssert.AreEqual(failureType, (maybeResult.Failure as FileDownloadFailure).FailureType, "Unexpected FileDownloadFailure.FailureType");
        }

        private PackageOnDisk CreateTestPackageOnDisk(bool includeScriptSpec, bool fromCache = false, string packageName = "Foo.Bar", string version = "1.2")
        {
            var pkgFolder = Path.Combine(TemporaryDirectory, "pkg", packageName + "." + version);

            var relativePaths = new List<RelativePath>
                                {
                                    CreateFile(pkgFolder, packageName + ".nuspec", "<nuspec />")
                                };

            if (includeScriptSpec)
            {
                relativePaths.Add(CreateFile(pkgFolder, Names.PackageConfigDsc,
$@"module({{
    name: ""{packageName}"",
    projects: [f`package.dsc`, f`util.dsc`]
}});"));
                relativePaths.Add(CreateFile(pkgFolder, "package.dsc", "export const x = 42;"));
                relativePaths.Add(CreateFile(pkgFolder, "util.dsc", "export const x = 42;"));
            }

            relativePaths.Add(CreateFile(pkgFolder, @"Folder\a.txt", "AAA"));
            relativePaths.Add(CreateFile(pkgFolder, @"Folder\b.txt", "BBB"));

            return new PackageOnDisk(
                m_testContext.PathTable,
                new NugetPackage
                {
                    Id = packageName,
                    Version = version
                },
                CreatePackageDownloadResult(fromCache, packageName, version, pkgFolder, relativePaths));
        }

        private PackageDownloadResult CreatePackageDownloadResult(bool fromCache, string packageName, string version, string pkgFolder, List<RelativePath> relativePaths)
        {
            return fromCache ?
                PackageDownloadResult.FromCache(
                    new PackageIdentity("nuget", packageName, version, string.Empty),
                    AbsolutePath.Create(m_testContext.PathTable, pkgFolder),
                    relativePaths,
                    packageName + version + pkgFolder)
                : PackageDownloadResult.FromRemote(
                    new PackageIdentity("nuget", packageName, version, string.Empty),
                    AbsolutePath.Create(m_testContext.PathTable, pkgFolder),
                    relativePaths,
                    packageName + version + pkgFolder);
        }

        private RelativePath CreateFile(string pkgFolder, string relativePath, string contents)
        {
            var filePath = Path.Combine(pkgFolder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, contents, Encoding.UTF8);
            return RelativePath.Create(m_testContext.PathTable.StringTable, relativePath);
        }

        private FrontEndHostController CreateFrontEndHostControllerForTesting()
        {
            return FrontEndHostController.CreateForTesting(
               m_testContext,
               new BasicFrontEndEngineAbstraction(m_testContext.PathTable, m_testContext.FileSystem),
               new ModuleRegistry(m_testContext.SymbolTable),
               Path.Combine(TemporaryDirectory, Names.ConfigDsc),
               outputDirectory: Path.Combine(TemporaryDirectory, "out"));
        }

        private WorkspaceNugetModuleResolver CreateWorkspaceNugetResolverForTesting()
        {
            var host = CreateFrontEndHostControllerForTesting();

            var nugetResolver = new WorkspaceNugetModuleResolver(m_testContext.StringTable, new FrontEndStatistics());
            nugetResolver.TryInitialize(host, m_testContext, new ConfigurationImpl(), new NugetResolverSettings(), new QualifierId[] { m_testContext.QualifierTable.EmptyQualifierId });

            return nugetResolver;
        }
    }

    /// <nodoc/>
    public static class NugetResolverUnitTestsExtensions
    {
        /// <summary>
        /// Configures the resolver based on a collection of packages on disk
        /// </summary>
        public static WorkspaceNugetModuleResolver ConfigureWithPackages(this WorkspaceNugetModuleResolver resolver, params PackageOnDisk[] packagesOnDisk)
        {
            var allPackages = packagesOnDisk.ToDictionary(kvp => kvp.Package.Id, kvp => kvp.Package);

            var allAnalyzedPackages = packagesOnDisk.ToDictionary(package => package.Package.Id, package => resolver.AnalyzeNugetPackage(package, false).Result);

            resolver.SetDownloadedPackagesForTesting(allAnalyzedPackages);

            return resolver;
        }
    }
}

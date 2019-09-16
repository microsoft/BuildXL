// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Engine;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration.Mutable;
using Newtonsoft.Json;
using Test.BuildXL.FrontEnd.Core;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public sealed class NugetCgManifestGeneratorTests 
    {
        private readonly FrontEndContext m_context;
        private readonly NugetCgManifestGenerator m_generator;
        private FrontEndHostController frontEndHostController;
        private FrontEndEngineAbstraction engine;
        TemporaryStorageTestBase storage;

        public NugetCgManifestGeneratorTests()
        {
            m_context = FrontEndContext.CreateInstanceForTesting();
            m_generator = new NugetCgManifestGenerator(m_context);
            //TestSerializationCompleteness temp = TestSerializationCompleteness();
            //frontEndHostController = CreateFrontEndHostControllerForTesting();
            //storage = TemporaryStorageTestBase.;
        }

        //private FrontEndHostController CreateFrontEndHostControllerForTesting()
        //{
        //    return FrontEndHostController.CreateForTesting(
        //       m_context,
        //       new BasicFrontEndEngineAbstraction(m_context.PathTable, m_context.FileSystem),
        //       new ModuleRegistry(m_context.SymbolTable),
        //       Path.Combine(TemporaryStorageTestBase.TemporaryDirectory, Names.ConfigDsc),
        //       outputDirectory: Path.Combine(TemporaryDirectory, "out"));
        //}

        [Fact]
        public void TestEmptyPackages()
        {
            MultiValueDictionary<string, Package> packages = new MultiValueDictionary<string, Package>();
            var manifest = m_generator.GenerateCgManifestForPackages(packages);

            var cgmanifest = new
            {
                Version = 1,
                Registrations = new object[0]
            };
            string expectedManifest = JsonConvert.SerializeObject(cgmanifest, Formatting.Indented);

            XAssert.IsTrue(m_generator.CompareForEquality(manifest, expectedManifest));
        }

        [Fact]
        public void TestSinglePackage()
        {
            //Package package = DsTest.CreatePackageFromConfig(m_context, path);
            //version:  p.Path.GetParent(Context.PathTable).GetName(Context.PathTable).ToString(Context.StringTable)
            //    [packageName] for the name
            //var package = NugetResolverUnitTests.CreateTestPackageOnDisk(includeScriptSpec: false, packageName: "System.Memory", version: "4.5.1");
            // TODO(rijul) check that manifest looks as expected for a single package;
            //             see NugetResolverUnitTests.cs for how to generate objects of type Package
        }

        [Fact]
        public void TestSorted()
        {
            // TODO(rijul) generate manifest for multiple packages and assert that the packages inside the manifest are sorted
        }

        [Fact]
        public void TestCompareForEquality()
        {
            string intendedManifest = @"{
  ""Version"": 1,
  ""Registrations"": [
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""Antlr4.Runtime.Standard"",
          ""Version"": ""4.7.2""
        }
      }
    },
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""Aria.Cpp.SDK"",
          ""Version"": ""8.5.6""
        }
      }
    },
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""ArtifactServices.App.Shared"",
          ""Version"": ""17.150.28901-buildid9382555""
        }
      }
    }
  ]
}
";
            string noSpaceManifest = @"{
""Version"":1,""Registrations"":[{
""Component"":{
""Type"":""NuGet"",
""NuGet"":{
""Name"":""Antlr4.Runtime.Standard"",
""Version"":""4.7.2""
}}},
{
""Component"":{
""Type"":""NuGet"",
""NuGet"":{
""Name"":""Aria.Cpp.SDK"",
""Version"":""8.5.6""
}}},
{
""Component"":{
""Type"":""NuGet"",
""NuGet"":{
""Name"":""ArtifactServices.App.Shared"",
""Version"":""17.150.28901-buildid9382555""
}}}]}
";
            XAssert.IsTrue(m_generator.CompareForEquality(noSpaceManifest, intendedManifest));
        }

        [Fact]
        public void TestCompareForEqualityInvalidFormat()
        {
            string validJson = "{ }";
            string inValidJson = "{ ";
            XAssert.IsFalse(m_generator.CompareForEquality(validJson, inValidJson));
        }

        //private Package CreatePackage(string packageName, string version)
        //{
        //    NugetPackage np = new NugetPackage
        //    {
        //        Id = packageName,
        //        Version = version
        //    };

        //    Package p = np


        //    PackageId id = new PackageId();
        //    AbsolutePath path = new AbsolutePath();
        //    IPackageDescriptor descriptor = new PackageDescriptor();


        //    return Package.Create(id, path, descriptor);

        //}
    }
}

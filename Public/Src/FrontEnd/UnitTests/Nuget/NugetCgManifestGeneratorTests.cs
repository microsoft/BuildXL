// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public sealed class NugetCgManifestGeneratorTests  : TemporaryStorageTestBase
    {
        private readonly FrontEndContext m_context;
        private readonly NugetCgManifestGenerator m_generator;

        public NugetCgManifestGeneratorTests()
        {
            m_context = FrontEndContext.CreateInstanceForTesting();
            m_generator = new NugetCgManifestGenerator(m_context);
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
            XAssert.IsTrue(NugetCgManifestGenerator.CompareForEquality(noSpaceManifest, intendedManifest));
        }

        [Fact]
        public void TestCompareForEqualityInvalidFormat()
        {
            string validJson = "{ }";
            string inValidJson = "{ ";
            XAssert.IsFalse(NugetCgManifestGenerator.CompareForEquality(validJson, inValidJson));
        }

        private Package CreatePackage(string version)
        {
            AbsolutePath path = AbsolutePath.Create(m_context.PathTable, TemporaryDirectory + $"\\random.package.name\\{version}\\nu.spec");
            var pathStr = path.ToString(m_context.PathTable);
            var id = PackageId.Create(StringId.Create(m_context.StringTable, pathStr));
            var desc = new PackageDescriptor();

            return Package.Create(id, path, desc);

        }

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

            XAssert.IsTrue(NugetCgManifestGenerator.CompareForEquality(manifest, expectedManifest));
        }

        [Fact]
        public void TestSinglePackage()
        {
            MultiValueDictionary<string, Package> packages = new MultiValueDictionary<string, Package>
            {
                { "test.package.name", CreatePackage("1.0.1") }
            };

            string expectedMainifest = @"
{
  ""Version"": 1,
  ""Registrations"": [
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""test.package.name"",
          ""Version"": ""1.0.1""
        }
      }
    }
  ]
}";

            XAssert.IsTrue(NugetCgManifestGenerator.CompareForEquality(expectedMainifest, m_generator.GenerateCgManifestForPackages(packages)));
        }

        [Fact]
        public void TestSorted()
        {
            MultiValueDictionary<string, Package> packages = new MultiValueDictionary<string, Package>
            {
                { "test.package.name", CreatePackage("2.0.1") },
                { "test.package.name", CreatePackage("1.0.2") },
                { "test.package.a", CreatePackage("5.1.1") },
                { "test.package.z", CreatePackage("1.0.0") },
                { "test.package.name", CreatePackage("1.0.1") },
                { "test.a.name", CreatePackage("10.0.1") },
                { "Dotnet-Runtime", CreatePackage("1.1.1") },
                { "DotNet.Glob", CreatePackage("1.1.1") },
            };

            string expectedMainifest = @"
{
  ""Version"": 1,
  ""Registrations"": [
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""DotNet.Glob"",
          ""Version"": ""1.1.1""
        }
      }
    },
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""Dotnet-Runtime"",
          ""Version"": ""1.1.1""
        }
      }
    },
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""test.a.name"",
          ""Version"": ""10.0.1""
        }
      }
    },
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""test.package.a"",
          ""Version"": ""5.1.1""
        }
      }
    },
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""test.package.name"",
          ""Version"": ""1.0.1""
        }
      }
    },
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""test.package.name"",
          ""Version"": ""1.0.2""
        }
      }
    },
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""test.package.name"",
          ""Version"": ""2.0.1""
        }
      }
    },
    {
      ""Component"": {
        ""Type"": ""NuGet"",
        ""NuGet"": {
          ""Name"": ""test.package.z"",
          ""Version"": ""1.0.0""
        }
      }
    }
  ]
}";

            XAssert.IsTrue(NugetCgManifestGenerator.CompareForEquality(expectedMainifest, m_generator.GenerateCgManifestForPackages(packages)));
        }
    }
}

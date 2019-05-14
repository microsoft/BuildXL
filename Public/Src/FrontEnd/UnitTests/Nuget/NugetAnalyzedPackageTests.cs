// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;
using Xunit;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public class NugetAnalyzedPackageTests
    {
        private readonly FrontEndContext m_context;
        private readonly NugetFrameworkMonikers m_monikers;
        private readonly PackageGenerator m_packageGenerator;

        private static readonly INugetPackage s_myPackage = new NugetPackage() { Id = "MyPkg", Version = "1.99" };
        private static readonly INugetPackage s_systemCollections = new NugetPackage() { Id = "System.Collections", Version = "4.0.11" };
        private static readonly INugetPackage s_systemCollectionsConcurrent = new NugetPackage() { Id = "System.Collections.Concurrent", Version = "4.0.12" };

        private static readonly Dictionary<string, INugetPackage> s_packagesOnConfig = new Dictionary<string, INugetPackage>
        {
            [s_myPackage.Id] = s_myPackage,
            [s_systemCollections.Id] = s_systemCollections,
            [s_systemCollectionsConcurrent.Id] = s_systemCollectionsConcurrent
        };

        public NugetAnalyzedPackageTests()
        {
            m_context = FrontEndContext.CreateInstanceForTesting();
            m_monikers = new NugetFrameworkMonikers(m_context.StringTable);
            m_packageGenerator = new PackageGenerator(m_context, m_monikers);
        }

        [Fact]
        public void ParseDependencies()
        {
            var pkg = AnalyzePackage(
                @"<?xml version='1.0' encoding='utf-8'?>
<package xmlns='http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'>
  <metadata>
    <id>MyPkg</id>
    <version>1.999</version>
    <dependencies>
      <dependency id='System.Collections' version='4.0.11' />
      <dependency id='System.Collections.Concurrent' version='4.0.12' />
    </dependencies>
  </metadata>
</package>",
                s_packagesOnConfig);

            Assert.Equal(2, pkg.Dependencies.Count);
            Assert.Equal("System.Collections", pkg.Dependencies.First().Id);
            Assert.Equal("4.0.11", pkg.Dependencies.First().Version);

            Assert.False(pkg.DependenciesPerFramework.ContainsKey(m_monikers.Net46));
        }

        [Fact]
        public void ParseDependenciesWithVersionRange()
        {
            var pkg = AnalyzePackage(
                @"<?xml version='1.0' encoding='utf-8'?>
<package xmlns='http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'>
  <metadata>
    <id>MyPkg</id>
    <version>1.999</version>
    <dependencies>
      <dependency id='System.Collections' version='[4.0.11]' />
      <dependency id='System.Collections.Concurrent' version='4.0.12' />
    </dependencies>
  </metadata>
</package>",
                s_packagesOnConfig);

            Assert.Equal(2, pkg.Dependencies.Count);
            Assert.Equal("System.Collections", pkg.Dependencies.First().Id);
            Assert.Equal("4.0.11", pkg.Dependencies.First().Version);

            Assert.False(pkg.DependenciesPerFramework.ContainsKey(m_monikers.Net46));
        }

        [Fact]
        public void ParseDependenciesWithInclusiveRange()
        {
            var pkg = AnalyzePackage(
                @"<?xml version='1.0' encoding='utf-8'?>
<package xmlns='http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'>
  <metadata>
    <id>MyPkg</id>
    <version>1.999</version>
    <dependencies>
      <dependency id='System.Collections' version='[3.0.0, 4.0.11]' />
      <dependency id='System.Collections.Concurrent' version='[4.0.0, 4.1.1]' />
    </dependencies>
  </metadata>
</package>",
                s_packagesOnConfig);

            Assert.Equal(2, pkg.Dependencies.Count);
            Assert.Equal("System.Collections", pkg.Dependencies.First().Id);
            Assert.Equal("4.0.11", pkg.Dependencies.First().Version);

            Assert.False(pkg.DependenciesPerFramework.ContainsKey(m_monikers.Net46));
        }

        [Fact]
        public void ParseDependenciesFromNuSpecWithGroups()
        {
            var pkg = AnalyzePackage(
                @"<?xml version='1.0' encoding='utf-8'?>
<package xmlns='http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'>
  <metadata>
    <id>MyPkg</id>
    <version>1.999</version>
    <dependencies>
      <group targetFramework='MonoAndroid1.0' />
      <group targetFramework='MonoTouch1.0' />
      <group targetFramework='.NETFramework4.5'>
        <dependency id='System.Collections' version='4.0.11' />
        <dependency id='System.Collections.Concurrent' version='4.0.12' />
      </group>
      <group targetFramework='.NETStandard1.1'>
        <dependency id='System.Collections' version='4.0.11' />
        <dependency id='System.Collections.Concurrent' version='4.0.12' />
      </group>
      <group targetFramework='.NETStandard1.3'>
        <dependency id='System.Collections' version='4.0.11' />
        <dependency id='System.Collections.Concurrent' version='4.0.12' />
      </group>
      <group targetFramework='Windows8.0' />
      <group targetFramework='WindowsPhoneApp8.1' />
    </dependencies>
  </metadata>
</package>",
                s_packagesOnConfig);

            Assert.Equal(0, pkg.Dependencies.Count);
            Assert.True(pkg.DependenciesPerFramework.ContainsKey(m_monikers.Net45));
            Assert.Equal(2, pkg.DependenciesPerFramework[m_monikers.Net45].Count);
            Assert.Equal("System.Collections", pkg.DependenciesPerFramework[m_monikers.Net45].First().Id);
            Assert.Equal("4.0.11", pkg.DependenciesPerFramework[m_monikers.Net45].First().Version);

            Assert.False(pkg.DependenciesPerFramework.ContainsKey(m_monikers.Net46));
        }

        [Fact]
        public void LibForNet45()
        {
            var packageRelativePath = R("lib", "net45", "MyPkg.dll");
            var pkg = AnalyzePackage(
                @"<?xml version='1.0' encoding='utf-8'?>
<package xmlns='http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'>
  <metadata>
    <id>MyPkg</id>
    <version>1.999</version>
    <dependencies>
    </dependencies>
  </metadata>
</package>",
                s_packagesOnConfig,
                packageRelativePath);

            Assert.Equal(1, pkg.Libraries.Count);
            Assert.Equal(packageRelativePath, pkg.Libraries[new NugetTargetFramework(m_monikers.Net45)].First().ToString(m_context.StringTable));
        }

        [Fact]
        public void TestManagedDependenciesImpliesAManagedPackage()
        {
            var pkg = AnalyzePackage(
                @"<?xml version='1.0' encoding='utf-8'?>
<package xmlns='http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'>
  <metadata>
    <id>MyPkg</id>
    <version>1.999</version>
    <dependencies>
      <group targetFramework='.NETFramework4.5'>
        <dependency id='System.Collections' version='4.0.11' />
        <dependency id='System.Collections.Concurrent' version='4.0.12' />
      </group>
      <group targetFramework='.NETFramework4.5.1'>
        <dependency id='System.Collections' version='4.0.11' />
        <dependency id='System.Collections.Concurrent' version='4.0.12' />
      </group>
    </dependencies>
  </metadata>
</package>",
                s_packagesOnConfig);

            Assert.True(pkg.IsManagedPackage);
            Assert.True(pkg.TargetFrameworks.Contains(m_monikers.Net45));
            Assert.True(pkg.TargetFrameworks.Contains(m_monikers.Net451));
        }

        private NugetAnalyzedPackage AnalyzePackage(string xml, Dictionary<string, INugetPackage> packagesOnConfig, params string[] relativePaths)
        {
            return m_packageGenerator.AnalyzePackage(xml, packagesOnConfig, relativePaths);
        }
    }
}

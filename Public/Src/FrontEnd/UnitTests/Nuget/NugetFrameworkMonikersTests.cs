// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;
using Xunit;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public class NugetFrameworkMonikersTest
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

        public NugetFrameworkMonikersTest()
        {
            m_context = FrontEndContext.CreateInstanceForTesting();
            m_monikers = new NugetFrameworkMonikers(m_context.StringTable);
            m_packageGenerator = new PackageGenerator(m_context, m_monikers);
        }

        [Fact]
        public void TestNuget451()
        {
            // Perform a random sample for some target framework moniker scenarios. Otherwise there are too many combinations.
            // This just guarantees a basic functionality of the moniker management and spec generator functionality.
            var stringTable = new PathTable().StringTable;
            var monikers = new NugetFrameworkMonikers(stringTable);

            // Public member
            Assert.Equal("net451", monikers.Net451.ToString(stringTable));
            Assert.Equal("net452", monikers.Net452.ToString(stringTable));

            // Is well known
            Assert.True(monikers.WellknownMonikers.Contains(monikers.Net451));

            // 4.5 is compatible with 4.5.1 and newer monikers, not older ones!
            var analyzedPackage = m_packageGenerator.AnalyzePackage(
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
    </dependencies>
  </metadata>
</package>", s_packagesOnConfig, new string[] { "lib/net45/my.dll" });

            List<PathAtom> compatibleTfms = new List<PathAtom>();
            NugetSpecGenerator.FindAllCompatibleFrameworkMonikers(analyzedPackage,
                (List<PathAtom> m) => compatibleTfms.AddRange(m),
                m_monikers.FullFrameworkVersionHistory,
                m_monikers.NetCoreVersionHistory);

            Assert.False(compatibleTfms.Contains(m_monikers.Net40));
            Assert.True(compatibleTfms.Contains(m_monikers.Net45));
            Assert.True(compatibleTfms.Contains(m_monikers.Net451));
            Assert.True(compatibleTfms.Contains(m_monikers.Net472));

            // Mapping tests
            Assert.Equal(monikers.Net451, monikers.TargetFrameworkNameToMoniker[".NETFramework4.5.1"]);
        }
    }
}

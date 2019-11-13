// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Xml.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.FrontEnd.Nuget
{
    internal class PackageGenerator
    {
        private readonly FrontEndContext m_context;
        private readonly NugetFrameworkMonikers m_monikers;

        public PackageGenerator(FrontEndContext context, NugetFrameworkMonikers monikers)
        {
            m_context = context;
            m_monikers = monikers;
        }

        public NugetAnalyzedPackage AnalyzePackage(string xml, Dictionary<string, INugetPackage> packagesOnConfig, params string[] relativePaths)
        {
            var nugetPackage = new NugetPackage() { Id = "TestPkg", Version = "1.999" };

            var paths = new List<RelativePath>();
            paths.Add(RelativePath.Create(m_context.StringTable, nugetPackage.Id + ".nuspec"));
            foreach (var relativePath in relativePaths)
            {
                paths.Add(RelativePath.Create(m_context.StringTable, relativePath));
            }

            var packageOnDisk = new PackageOnDisk(
                m_context.PathTable,
                nugetPackage,
                PackageDownloadResult.FromRemote(
                    new PackageIdentity("nuget", nugetPackage.Id, nugetPackage.Version, nugetPackage.Alias),
                    AbsolutePath.Create(m_context.PathTable, A("X", "Pkgs", "TestPkg", "1.999", "TestPkg.nuspec")),
                    paths,
                    "testPackageHash"));

            return NugetAnalyzedPackage.TryAnalyzeNugetPackage(m_context, m_monikers, XDocument.Parse(xml), packageOnDisk, packagesOnConfig, false);
        }
    }
}

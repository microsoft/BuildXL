// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public sealed class NugetCgManifestGeneratorTests
    {
        private readonly FrontEndContext m_context;
        private readonly NugetCgManifestGenerator m_generator;

        MultiValueDictionary<string, Package> packages = new MultiValueDictionary<string, Package>();

        public NugetCgManifestGeneratorTests()
        {
            m_context = FrontEndContext.CreateInstanceForTesting();
            m_generator = new NugetCgManifestGenerator(m_context);
        }

        [Fact]
        public void TestEmptyPackages()
        {
            var manifest = m_generator.GenerateCgManifestForPackages(packages);
            // TODO(rijul) 
        }

        [Fact]
        public void TestSinglePackage()
        {
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
            // TODO(rijul) make sure that NugetCgManifestGenerator.CompareForEquality is white space agnostic and case insensitive
        }

        [Fact]
        public void TestCompareForEqualityInvalidFormat()
        {
            
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;
using Xunit;
using Xunit.Abstractions;
using PathGeneratorUtilities = Test.BuildXL.TestUtilities.Xunit.PathGeneratorUtilities;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public class NuSpecGeneratorTests
    {
        private readonly ITestOutputHelper m_output;
        private readonly FrontEndContext m_context;
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

        public NuSpecGeneratorTests(ITestOutputHelper output)
        {
            m_output = output;
            m_context = FrontEndContext.CreateInstanceForTesting();

            var monikers = new NugetFrameworkMonikers(m_context.StringTable);
            m_packageGenerator = new PackageGenerator(m_context, monikers);
        }

        [Fact]
        public void GenerateNuSpec()
        {
            var expectedpackageRoot = "../../../pkgs/TestPkg.1.999";

            var pkg = m_packageGenerator.AnalyzePackage(
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
                s_packagesOnConfig, new string[] { "lib/net45/my.dll", "lib/net451/my.dll"});

            var spec = new NugetSpecGenerator(m_context.PathTable, pkg).CreateScriptSourceFile(pkg);
            var text = spec.ToDisplayStringV2();
            m_output.WriteLine(text);

            string expectedSpec = string.Format(@"import {{Transformer}} from ""Sdk.Transformers"";
import * as Managed from ""Sdk.Managed"";

export declare const qualifier: {{targetFramework: ""net45"" | ""net451"" | ""net452"" | ""net46"" | ""net461"" | ""net462"" | ""net472""}};

const packageRoot = Contents.packageRoot;

namespace Contents {{
    export declare const qualifier: {{
    }};
    export const packageRoot = d`../../../pkgs/TestPkg.1.999`;
    @@public
    export const all: StaticDirectory = Transformer.sealDirectory(
        packageRoot,
        [f`${{packageRoot}}/lib/net45/my.dll`, f`${{packageRoot}}/lib/net451/my.dll`, f`${{packageRoot}}/TestPkg.nuspec`]
    );
}}

@@public
export const pkg: Managed.ManagedNugetPackage = (() => {{
    switch (qualifier.targetFramework) {{
        case ""net45"":
            return Managed.Factory.createNugetPackage(
                ""TestPkg"",
                ""1.999"",
                Contents.all,
                [Managed.Factory.createBinaryFromFiles(f`${{packageRoot}}/lib/net45/my.dll`)],
                [Managed.Factory.createBinaryFromFiles(f`${{packageRoot}}/lib/net45/my.dll`)],
                [
                    ...addIfLazy(qualifier.targetFramework === ""net45"", () => [importFrom(""System.Collections"").pkg, importFrom(""System.Collections.Concurrent"").pkg]),
                ]
            );
        case ""net451"":
        case ""net452"":
        case ""net46"":
        case ""net461"":
        case ""net462"":
        case ""net472"":
            return Managed.Factory.createNugetPackage(
                ""TestPkg"",
                ""1.999"",
                Contents.all,
                [Managed.Factory.createBinaryFromFiles(f`${{packageRoot}}/lib/net451/my.dll`)],
                [Managed.Factory.createBinaryFromFiles(f`${{packageRoot}}/lib/net451/my.dll`)],
                [
                    ...addIfLazy(qualifier.targetFramework === ""net451"", () => [importFrom(""System.Collections"").pkg, importFrom(""System.Collections.Concurrent"").pkg]),
                ]
            );
        default:
            Contract.fail(""Unsupported target framework"");
    }};
}}
)();", expectedpackageRoot);
            Assert.Equal(expectedSpec, text);
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;
using Xunit;
using Xunit.Abstractions;
using Test.BuildXL.TestUtilities.Xunit;
using System;
using BuildXL.Utilities;

namespace Test.BuildXL.FrontEnd.Nuget
{
    public class NuSpecGeneratorTests
    {
        private const int CurrentSpecGenVersion = 12;

        private readonly ITestOutputHelper m_output;
        private readonly FrontEndContext m_context;
        private readonly PackageGenerator m_packageGenerator;
        private readonly Dictionary<string, string> m_repositories;
        private static readonly INugetPackage s_myPackage = new NugetPackage() { Id = "MyPkg", Version = "1.99" };
        private static readonly INugetPackage s_systemCollections = new NugetPackage() { Id = "System.Collections", Version = "4.0.11" };
        private static readonly INugetPackage s_systemCollectionsConcurrent = new NugetPackage() { Id = "System.Collections.Concurrent", Version = "4.0.12" };
        private static readonly INugetPackage s_newtonsoftJson = new NugetPackage() { Id = "Newtonsoft.Json", Version = "10.0.0" };

        private static readonly Dictionary<string, INugetPackage> s_packagesOnConfig = new Dictionary<string, INugetPackage>
        {
            [s_myPackage.Id] = s_myPackage,
            [s_systemCollections.Id] = s_systemCollections,
            [s_systemCollectionsConcurrent.Id] = s_systemCollectionsConcurrent,
            [s_newtonsoftJson.Id] = s_newtonsoftJson
        };

        public NuSpecGeneratorTests(ITestOutputHelper output)
        {
            m_output = output;
            m_context = FrontEndContext.CreateInstanceForTesting();

            var monikers = new NugetFrameworkMonikers(m_context.StringTable);
            m_packageGenerator = new PackageGenerator(m_context, monikers);

            m_repositories = new Dictionary<string, string>() { ["BuildXL"] = "https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json" };
        }

        [Fact]
        public void GenerateNuSpec()
        {
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
      <group targetFramework='.NETStandard2.0'>
        <dependency id='Newtonsoft.Json' version='10.0.0' exclude='Build,Analyzers' />
      </group>
    </dependencies>
  </metadata>
</package>",
                s_packagesOnConfig, new string[] { "lib/net45/my.dll", "lib/net451/my.dll",  "lib/netstandard2.0/my.dll"});

            var spec = new NugetSpecGenerator(m_context.PathTable, pkg, m_repositories, AbsolutePath.Invalid).CreateScriptSourceFile(pkg);
            var text = spec.ToDisplayStringV2();
            m_output.WriteLine(text);

            string expectedSpec = $@"import * as NugetDownloader from ""BuildXL.Tools.NugetDownloader"";
import * as Managed from ""Sdk.Managed"";

export declare const qualifier: {{
    targetFramework: ""net45"" | ""net451"" | ""net452"" | ""net46"" | ""net461"" | ""net462"" | ""net472"" | ""netstandard2.0"" | ""netcoreapp2.0"" | ""netcoreapp2.1"" | ""netcoreapp2.2"" | ""netcoreapp3.0"" | ""netcoreapp3.1"" | ""net5.0"" | ""net6.0"" | ""netstandard2.1"",
    targetRuntime: ""win-x64"" | ""osx-x64"" | ""linux-x64"",
}};

namespace Contents {{
    export declare const qualifier: {{
    }};
    const outputDir: Directory = Context.getNewOutputDirectory(""nuget"");
    @@public
    export const all: StaticDirectory = NugetDownloader.downloadPackage({{
        id: ""TestPkg"",
        version: ""1.999"",
        downloadDirectory: outputDir,
        extractedFiles: [
            r`TestPkg.nuspec`,
            r`lib/net45/my.dll`,
            r`lib/net451/my.dll`,
            r`lib/netstandard2.0/my.dll`,
        ],
        repositories: [[""BuildXL"", ""https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json""]],
    }});
}}

@@public
export const pkg: Managed.ManagedNugetPackage = (() => {{
    switch (qualifier.targetFramework) {{
        case ""net45"":
            return Managed.Factory.createNugetPackage(
                ""TestPkg"",
                ""1.999"",
                Contents.all,
                [Managed.Factory.createBinaryFromFiles(Contents.all.getFile(r`lib/net45/my.dll`))],
                [Managed.Factory.createBinaryFromFiles(Contents.all.getFile(r`lib/net45/my.dll`))],
                [
                    importFrom(""System.Collections"").pkg,
                    importFrom(""System.Collections.Concurrent"").pkg,
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
                [Managed.Factory.createBinaryFromFiles(Contents.all.getFile(r`lib/net451/my.dll`))],
                [Managed.Factory.createBinaryFromFiles(Contents.all.getFile(r`lib/net451/my.dll`))],
                [
                    importFrom(""System.Collections"").pkg,
                    importFrom(""System.Collections.Concurrent"").pkg,
                ]
            );
        case ""netstandard2.0"":
        case ""netcoreapp2.0"":
        case ""netcoreapp2.1"":
        case ""netcoreapp2.2"":
        case ""netcoreapp3.0"":
        case ""netcoreapp3.1"":
        case ""net5.0"":
        case ""net6.0"":
        case ""netstandard2.1"":
            return Managed.Factory.createNugetPackage(
                ""TestPkg"",
                ""1.999"",
                Contents.all,
                [Managed.Factory.createBinaryFromFiles(Contents.all.getFile(r`lib/netstandard2.0/my.dll`))],
                [
                    Managed.Factory.createBinaryFromFiles(Contents.all.getFile(r`lib/netstandard2.0/my.dll`)),
                ],
                [...addIfLazy(qualifier.targetFramework === ""netstandard2.0"", () => [importFrom(""Newtonsoft.Json"").pkg])]
            );
        default:
            Contract.fail(""Unsupported target framework"");
    }};
}}
)();";
            XAssert.AreEqual(expectedSpec.Trim(), text.Trim());

            const string CurrentSpecHash = "1291F51E8E59ED9C2F967C01D49CC39FE6DEB9D8";
            ValidateCurrentSpecGenVersion(expectedSpec, CurrentSpecHash);
        }

        [Fact]
        public void GenerateNuSpecForStub()
        {
            var pkg = m_packageGenerator.AnalyzePackageStub(s_packagesOnConfig);
            var spec = new NugetSpecGenerator(m_context.PathTable, pkg, m_repositories, AbsolutePath.Invalid).CreateScriptSourceFile(pkg);
            var text = spec.ToDisplayStringV2();
            m_output.WriteLine(text);

            string expectedSpec = @"import * as NugetDownloader from ""BuildXL.Tools.NugetDownloader"";

export declare const qualifier: {
    targetFramework: ""net10"" | ""net11"" | ""net20"" | ""net35"" | ""net40"" | ""net45"" | ""net451"" | ""net452"" | ""net46"" | ""net461"" | ""net462"" | ""net472"" | ""netstandard1.0"" | ""netstandard1.1"" | ""netstandard1.2"" | ""netstandard1.3"" | ""netstandard1.4"" | ""netstandard1.5"" | ""netstandard1.6"" | ""netstandard2.0"" | ""netcoreapp2.0"" | ""netcoreapp2.1"" | ""netcoreapp2.2"" | ""netcoreapp3.0"" | ""netcoreapp3.1"" | ""net5.0"" | ""net6.0"" | ""netstandard2.1"",
    targetRuntime: ""win-x64"" | ""osx-x64"" | ""linux-x64"",
};

namespace Contents {
    export declare const qualifier: {
    };
    const outputDir: Directory = Context.getNewOutputDirectory(""nuget"");
    @@public
    export const all: StaticDirectory = NugetDownloader.downloadPackage({
        id: ""TestPkgStub"",
        version: ""1.999"",
        downloadDirectory: outputDir,
        extractedFiles: [],
        repositories: [[""BuildXL"", ""https://pkgs.dev.azure.com/cloudbuild/_packaging/BuildXL.Selfhost/nuget/v3/index.json""]],
    });
}

@@public
export const pkg: NugetPackage = {
    contents: Contents.all,
    dependencies: [],
    version: ""1.999"",
};";
            XAssert.ArrayEqual(SplitToLines(expectedSpec), SplitToLines(text));

            const string CurrentSpecHash = "389BC336BD4B206394E01B1E76A859E6561CE30D";
            ValidateCurrentSpecGenVersion(expectedSpec, CurrentSpecHash);
        }

        private void ValidateCurrentSpecGenVersion(string expectedSpec, string currentSpecHash)
        {
            var hashingHelper = new global::BuildXL.Storage.Fingerprints.HashingHelper(m_context.PathTable, recordFingerprintString: false);
            hashingHelper.Add(expectedSpec);
            var hash = BitConverter.ToString(hashingHelper.GenerateHashBytes()).Replace("-", string.Empty);

            if (currentSpecHash != hash)
            {
                var hasFormatVersionIncreased = NugetSpecGenerator.SpecGenerationFormatVersion > CurrentSpecGenVersion;
                if (!hasFormatVersionIncreased)
                {
                    XAssert.Fail(
$@"
**********************************************************************************
** It looks like NuGet spec generation has changed but the version of 
** '{nameof(NugetSpecGenerator.SpecGenerationFormatVersion)}.{nameof(NugetSpecGenerator)}' didn't increase.
**
** Please bump up the spec generation format version from {CurrentSpecGenVersion} to {CurrentSpecGenVersion + 1} and then
** update the '{nameof(currentSpecHash)}' and '{nameof(CurrentSpecGenVersion)}' values in this
** test to '{hash}' and '{CurrentSpecGenVersion + 1}' respectively.
**********************************************************************************");
                }
                else
                {
                    var lines = new[]
                    {
                        $"Congratulations on remembering to increment '{nameof(NugetSpecGenerator.SpecGenerationFormatVersion)}.{nameof(NugetSpecGenerator)}'",
                        $"after updating NuGet spec generator!",
                        $"",
                        $"To keep this reminder working, please update the '{nameof(currentSpecHash)}' and '{nameof(CurrentSpecGenVersion)}'",
                        $"values in this unit test to '{hash}' and '{NugetSpecGenerator.SpecGenerationFormatVersion}' respectively.",
                    };
                    const int width = 94;
                    var fst = " " + String.Concat(Enumerable.Repeat("_", width + 2)) + " ";
                    var snd = $"/ {' ',-width} \\";
                    var aligned = lines.Select(l => $"| {l,-width} |");
                    var last = "\\" + String.Concat(Enumerable.Repeat("_", width + 2)) + "/";
                    XAssert.Fail(string.Join(Environment.NewLine, new[] { fst, snd }.Concat(aligned).Concat(new[] { last })));
                }
            }
        }

        private string[] SplitToLines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}

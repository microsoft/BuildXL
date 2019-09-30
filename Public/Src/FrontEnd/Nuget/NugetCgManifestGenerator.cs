// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// NugetCgManifestGenerator is used for creation and comparasion of a manifest file for Component Governance.
    /// The cgmanifest file contains information about all the Nuget Packages used in BuildXL with all their versions in use
    /// The cgmanifest file is used by Component Governance to determine security risks within components used by BuildXL
    /// This manifest file will only be picked up for Component Governance if it is named "cgmanifest.json" as per cg documentation: https://docs.opensource.microsoft.com/tools/cg.html
    /// </summary>
    public sealed class NugetCgManifestGenerator
    {
        private FrontEndContext Context { get; }

        /// <nodoc />
        public NugetCgManifestGenerator(FrontEndContext context)
        {
            Context = context;
        }

        /// <summary>
        /// Generates json as an indented string containing all the NuGet package names and versions used in BuildXL
        /// </summary>
        public string GenerateCgManifestForPackages(MultiValueDictionary<string, Package> packages)
        {
            var components = packages
                .Keys
                .SelectMany(nugetName => packages[nugetName].Select(package => new NugetPackageAndVersionStore(nugetName, ExtractNugetVersion(package))))
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ThenBy(c => c.Version, StringComparer.Ordinal)
                .Select(c => ToNugetComponent(c.Name, c.Version))
                .ToList();

            var cgmanifest = new
            {
                Version = 1,
                Registrations = components
            };

            var formatted = JsonConvert.SerializeObject(cgmanifest, Formatting.Indented);

            // It's not easy to tell JsonConvert what to use for Newline.  By default, it uses Environment.Newline,
            // which means "\r\n" on Windows and "\n" on Mac.  We need this to be consistent across platforms.
            // We use Windows new line separator across the board for backward compatibility reasons.
            const string WindowsNewline = "\r\n";
            return Environment.NewLine != WindowsNewline
                ? formatted.Replace(Environment.NewLine, WindowsNewline)
                : formatted;
        }

        /// <summary>
        /// Compares <paramref name="lhsManifest"/> and <paramref name="rhsManifest"/> for equality;
        /// returns true if they are equal, and false otherwise. 
        /// 
        /// This equality check is case-insensitive and white space agnostic.
        /// </summary>
        public static bool CompareForEquality(string lhsManifest, string rhsManifest)
        {
            if (lhsManifest == null || rhsManifest == null)
            {
                return false;
            }

            try
            {
                return JToken.DeepEquals(JObject.Parse(lhsManifest), JObject.Parse(rhsManifest));
            }
            catch (JsonReaderException)
            {
                // The existing Manifest file was in invalid JSON format.
                // Hence it does not match.
                return false;
            }
            
        }

        private string ExtractNugetVersion(Package p)
        {
            // Relies on the folder structure created by the Nuget resolver.
            return p.Path.GetParent(Context.PathTable).GetName(Context.PathTable).ToString(Context.StringTable);
        }

        private object ToNugetComponent(string name, string version)
        {
            return new
            {
                Component = new
                {
                    Type = "NuGet",
                    NuGet = new
                    {
                        Name = name,
                        Version = version
                    }
                }
            };
        }

        private class NugetPackageAndVersionStore {
            public string Name { get; }
            public string Version { get; }

            public NugetPackageAndVersionStore(string name, string version) {
                Name = name;
                Version = version;
            }
        }
    }
}

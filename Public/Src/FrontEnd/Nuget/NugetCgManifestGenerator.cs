// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// NugetCgManifestGenerator is used for creation & comparasion of the cgmanifest.json file.
    /// cgmanifest.json contains all the Nuget Packages used in BuildXL with all their versions in use
    /// cgmanifest.json is used by Component Governance in Cloud Build to determine security risks within components used by BuildXL
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
        /// Generates json as a string containing Nuget package and version information for all package used in BuildXL
        /// To be stored in cgmanifest.json only if output is different from the existing cgmanifest.json
        /// </summary>
        public string GenerateCgManifestForPackages(MultiValueDictionary<string, Package> packages)
        {
            var components = packages
                .Keys
                .SelectMany(nugetName => packages[nugetName].Select(package => new NugetPackageAndVersionStore(nugetName, ExtractNugetVersion(package))))
                .OrderBy(c => c.Name)
                .ThenBy(c => c.Version)
                .Select(c => ToNugetComponent(c.Name, c.Version))
                .ToList();

            var cgmanifest = new
            {
                Version = 1,
                Registrations = components
            };

            return JsonConvert.SerializeObject(cgmanifest, Formatting.Indented);
        }

        /// <summary>
        /// Compares <paramref name="lhsManifest"/> and <paramref name="rhsManifest"/> for equality;
        /// returns true if they are equal, and false otherwise. 
        /// 
        /// This equality check is case-insensitive and white space agnostic.
        /// </summary>
        public bool CompareForEquality(string lhsManifest, string rhsManifest)
        {
            return JToken.DeepEquals(JObject.Parse(lhsManifest), JObject.Parse(rhsManifest));
        }

        private string ExtractNugetVersion(Package p)
        {
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

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

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// TODO(rijul)
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
        /// TODO(rijul)
        /// </summary>
        public string GenerateCgManifestForPackages(MultiValueDictionary<string, Package> packages)
        {
            List<SimplePackage> components = new List<SimplePackage>();

            foreach (string nugetName in packages.Keys)
            {
                IReadOnlyList<Package> multiPackages;
                packages.TryGetValue(nugetName, out multiPackages);
                foreach (Package package in multiPackages) {
                    components.Add(new SimplePackage(nugetName, ExtractNugetVersion(package)));
                }
            }

            components = components.OrderBy(c => c.Name).ThenBy(c => c.Version).ToList();

            var cgmanifest = new
            {
                Version = 1,
                Registrations = ToNugetComponentArray(components)
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
            // TODO(rijul)
            return false;
        }

        private string ExtractNugetVersion(Package p)
        {
            // TODO(rijul) see if there is a more explicit way to get the version from the package instead of 
            //             extracting it from the path of the package spec
            return p.Path.GetParent(Context.PathTable).GetName(Context.PathTable).ToString(Context.StringTable);
        }

        private object[] ToNugetComponentArray(List<SimplePackage> sortedComponents)
        {
            List<object> components = new List<object>();

            foreach (SimplePackage package in sortedComponents) {
                components.Add(
                    new
                    {
                        Component = new
                        {
                            Type = "NuGet",
                            NuGet = new
                            {
                                Name = package.Name,
                                Version = package.Version
                            }
                        }
                    }
                );
            }

            return components.ToArray();
        }

        private class SimplePackage {
            public string Name { get; set; }
            public string Version { get; set; }

            public SimplePackage(string name, string version) {
                Name = name;
                Version = version;
            }
        }
    }
}

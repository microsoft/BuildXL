// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Sdk;
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
        public string GenerateCgManifestForPackages(IEnumerable<Package> packages)
        {
            var components = packages
                .OrderBy(p => p.Id.Name.ToString(Context.StringTable))
                .Select(p => ToNugetComponent(p.Id.Name.ToString(Context.StringTable), ExtractNugetVersion(p)))
                .ToArray();

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
            // TODO(rijul)
            return false;
        }

        private string ExtractNugetVersion(Package p)
        {
            // TODO(rijul) see if there is a more explicit way to get the version from the package instead of 
            //             extracting it from the path of the package spec
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
    }
}

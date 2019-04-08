// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    ///     Collection of cached Nuget Framework Monikers
    /// </summary>
    public sealed class NugetFrameworkMonikers
    {
        /// <nodoc />
        public PathAtom Net10 { get; }
        
        /// <nodoc />
        public PathAtom Net11 { get; }

        /// <nodoc />
        public PathAtom Net20 { get; }

        /// <nodoc />
        public PathAtom Net35 { get; }

        /// <nodoc />
        public PathAtom Net40 { get; }

        /// <nodoc />
        public PathAtom Net45 { get; }

        /// <nodoc />
        public PathAtom Net451 { get; }

        /// <nodoc />
        public PathAtom Net452 { get; }

        /// <nodoc />
        public PathAtom Net46 { get; }

        /// <nodoc />
        public PathAtom Net461 { get; }

        /// <nodoc />
        public PathAtom Net462 { get; }

        /// <nodoc />
        public PathAtom Net472 { get; }

        /// <nodoc />
        public PathAtom NetCore { get; }

        /// <nodoc />
        public PathAtom NetStandard10 { get; }

        /// <nodoc />
        public PathAtom NetStandard11 { get; }

        /// <nodoc />
        public PathAtom NetStandard12 { get; }

        /// <nodoc />
        public PathAtom NetStandard13 { get; }

        /// <nodoc />
        public PathAtom NetStandard14 { get; }

        /// <nodoc />
        public PathAtom NetStandard15 { get; }

        /// <nodoc />
        public PathAtom NetStandard16 { get; }

        /// <nodoc />
        public PathAtom NetStandard20 { get; }

        /// <nodoc />
        public PathAtom NetCoreApp20 { get; }

        /// <nodoc />
        public PathAtom NetCoreApp21 { get; }

        /// <nodoc />
        public PathAtom NetCoreApp22 { get; }

        /// <nodoc />
        public PathAtom BuildFolderName { get; }

        /// <nodoc />
        public PathAtom LibFolderName { get; }

        /// <nodoc />
        public PathAtom RefFolderName { get; }

        /// <nodoc />
        public HashSet<PathAtom> WellknownMonikers { get; }

        /// <summary>
        /// Compatibility matrix. If the key is desired, the values are ordered by preference of matches
        /// </summary>
        public MultiValueDictionary<PathAtom, PathAtom> CompatibilityMatrix { get; }

        /// <nodoc />
        public Dictionary<string, PathAtom> TargetFrameworkNameToMoniker { get; }

        /// <nodoc />
        public NugetFrameworkMonikers(StringTable stringTable)
        {
            LibFolderName = PathAtom.Create(stringTable, "lib");
            RefFolderName = PathAtom.Create(stringTable, "ref");

            WellknownMonikers = new HashSet<PathAtom>();
            CompatibilityMatrix = new MultiValueDictionary<PathAtom, PathAtom>();
            TargetFrameworkNameToMoniker = new Dictionary<string, PathAtom>();

            NetStandard10 = Register(stringTable, "netstandard1.0", ".NETStandard1.0");
            NetStandard11 = Register(stringTable, "netstandard1.1", ".NETStandard1.1");
            NetStandard12 = Register(stringTable, "netstandard1.2", ".NETStandard1.2");
            NetStandard13 = Register(stringTable, "netstandard1.3", ".NETStandard1.3");
            NetStandard14 = Register(stringTable, "netstandard1.4", ".NETStandard1.4");
            NetStandard15 = Register(stringTable, "netstandard1.5", ".NETStandard1.5");
            NetStandard16 = Register(stringTable, "netstandard1.6", ".NETStandard1.6");
            NetStandard20 = Register(stringTable, "netstandard2.0", ".NETStandard2.0");

            NetCoreApp20 = Register(stringTable, "netcoreapp2.0", ".NETCoreApp2.0");
            NetCoreApp21 = Register(stringTable, "netcoreapp2.1", ".NETCoreApp2.1");
            NetCoreApp22 = Register(stringTable, "netcoreapp2.2", ".NETCoreApp2.2");

            Net10 = Register(stringTable, "net10", ".NETFramework1.0");
            Net11 = Register(stringTable, "net11", ".NETFramework1.1");
            Net20 = Register(stringTable, "net20", ".NETFramework2.0");
            Net35 = Register(stringTable, "net35", ".NETFramework3.5");
            Net40 = Register(stringTable, "net40", ".NETFramework4.0");

            Net45 = Register(stringTable, "net45", ".NETFramework4.5");
            Net451 = Register(stringTable, "net451", ".NETFramework4.5.1");
            Net452 = Register(stringTable, "net452", ".NETFramework4.5.2");

            Net46 = Register(stringTable, "net46", ".NETFramework4.6");
            Net461 = Register(stringTable, "net461", ".NETFramework4.6.1");
            Net462 = Register(stringTable, "net462", ".NETFramework4.6.2");
            Net472 = Register(stringTable, "net472", ".NETFramework4.7.2");

            RegisterCompatibility(Net451, Net45, Net40, Net35, Net20, NetStandard12, NetStandard11, NetStandard10, Net11, Net10);
            //RegisterCompatibility(Net452, Net451, Net45, Net40, Net35);
            //RegisterCompatibility(Net46, NetStandard13, NetStandard12, NetStandard11, NetStandard10, Net452, Net451, Net45, Net40, Net35);
            // The fallback logic is: to use .net 4x version for .net 4.6.1
            RegisterCompatibility(Net461, Net46, Net452, Net451, Net45, Net40, NetStandard20, NetStandard16, NetStandard15, NetStandard14, NetStandard13, NetStandard12, NetStandard11, NetStandard10, Net35, Net20, Net11, Net10);

            RegisterCompatibility(Net472, Net461, Net46, Net452, Net451, Net45, Net40, NetStandard20, NetStandard16, NetStandard15, NetStandard14, NetStandard13, NetStandard12, NetStandard11, NetStandard10, Net35, Net20, Net11, Net10);

            RegisterCompatibility(NetStandard20, NetStandard16, NetStandard15, NetStandard14, NetStandard13, NetStandard12, NetStandard11, NetStandard10);
            
            RegisterCompatibility(NetCoreApp22, NetCoreApp21, NetCoreApp20, NetStandard20, NetStandard16, NetStandard15, NetStandard14, NetStandard13, NetStandard12, NetStandard11, NetStandard10);
        }

        private PathAtom Register(StringTable stringTable, string smallMoniker, string largeMoniker)
        {
            var pathAtom = PathAtom.Create(stringTable, smallMoniker);
            TargetFrameworkNameToMoniker.Add(largeMoniker, pathAtom);
            WellknownMonikers.Add(pathAtom);

            return pathAtom;
        }

        private void RegisterCompatibility(PathAtom moniker, params PathAtom[] compatibility)
        {
            if (compatibility != null && compatibility.Length > 0)
            {
                CompatibilityMatrix.Add(moniker, compatibility);
            }
        }
    }
}

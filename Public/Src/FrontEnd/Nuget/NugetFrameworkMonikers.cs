// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
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
        public PathAtom NetStandard21 { get; }

        /// <nodoc />
        public PathAtom NetCoreApp20 { get; }

        /// <nodoc />
        public PathAtom NetCoreApp21 { get; }

        /// <nodoc />
        public PathAtom NetCoreApp22 { get; }

        /// <nodoc />
        public PathAtom NetCoreApp30 { get; }

        /// <nodoc />
        public PathAtom BuildFolderName { get; }

        /// <nodoc />
        public PathAtom LibFolderName { get; }

        /// <nodoc />
        public PathAtom RefFolderName { get; }

        /// <nodoc />
        public HashSet<PathAtom> WellknownMonikers { get; }

        public readonly List<PathAtom> FullFrameworkVersionHistory;
        public readonly List<PathAtom> NetCoreVersionHistory;

        /// <nodoc />
        public Dictionary<string, PathAtom> TargetFrameworkNameToMoniker { get; }

        /// <nodoc />
        public NugetFrameworkMonikers(StringTable stringTable)
        {
            LibFolderName = PathAtom.Create(stringTable, "lib");
            RefFolderName = PathAtom.Create(stringTable, "ref");

            WellknownMonikers = new HashSet<PathAtom>();
            TargetFrameworkNameToMoniker = new Dictionary<string, PathAtom>();
            FullFrameworkVersionHistory = new List<PathAtom>();
            NetCoreVersionHistory = new List<PathAtom>();
            
            NetStandard10 = Register(stringTable, "netstandard1.0", ".NETStandard1.0", ref NetCoreVersionHistory);
            NetStandard11 = Register(stringTable, "netstandard1.1", ".NETStandard1.1", ref NetCoreVersionHistory);
            NetStandard12 = Register(stringTable, "netstandard1.2", ".NETStandard1.2", ref NetCoreVersionHistory);
            NetStandard13 = Register(stringTable, "netstandard1.3", ".NETStandard1.3", ref NetCoreVersionHistory);
            NetStandard14 = Register(stringTable, "netstandard1.4", ".NETStandard1.4", ref NetCoreVersionHistory);
            NetStandard15 = Register(stringTable, "netstandard1.5", ".NETStandard1.5", ref NetCoreVersionHistory);
            NetStandard16 = Register(stringTable, "netstandard1.6", ".NETStandard1.6", ref NetCoreVersionHistory);
            NetStandard20 = Register(stringTable, "netstandard2.0", ".NETStandard2.0", ref NetCoreVersionHistory);
            NetCoreApp20  = Register(stringTable, "netcoreapp2.0",  ".NETCoreApp2.0", ref NetCoreVersionHistory);
            NetCoreApp21  = Register(stringTable, "netcoreapp2.1",  ".NETCoreApp2.1", ref NetCoreVersionHistory);
            NetCoreApp22  = Register(stringTable, "netcoreapp2.2",  ".NETCoreApp2.2", ref NetCoreVersionHistory);
            NetCoreApp30  = Register(stringTable, "netcoreapp3.0",  ".NETCoreApp3.0", ref NetCoreVersionHistory);
            NetStandard21 = Register(stringTable, "netstandard2.1", ".NETStandard2.1", ref NetCoreVersionHistory);

            Net10  = Register(stringTable, "net10",  ".NETFramework1.0", ref FullFrameworkVersionHistory);
            Net11  = Register(stringTable, "net11",  ".NETFramework1.1", ref FullFrameworkVersionHistory);
            Net20  = Register(stringTable, "net20",  ".NETFramework2.0", ref FullFrameworkVersionHistory);
            Net35  = Register(stringTable, "net35",  ".NETFramework3.5", ref FullFrameworkVersionHistory);
            Net40  = Register(stringTable, "net40",  ".NETFramework4.0", ref FullFrameworkVersionHistory);
            Net45  = Register(stringTable, "net45",  ".NETFramework4.5", ref FullFrameworkVersionHistory);
            Net451 = Register(stringTable, "net451", ".NETFramework4.5.1", ref FullFrameworkVersionHistory);
            Net452 = Register(stringTable, "net452", ".NETFramework4.5.2", ref FullFrameworkVersionHistory);
            Net46  = Register(stringTable, "net46",  ".NETFramework4.6", ref FullFrameworkVersionHistory);
            Net461 = Register(stringTable, "net461", ".NETFramework4.6.1", ref FullFrameworkVersionHistory);
            Net462 = Register(stringTable, "net462", ".NETFramework4.6.2", ref FullFrameworkVersionHistory);
            Net472 = Register(stringTable, "net472", ".NETFramework4.7.2", ref FullFrameworkVersionHistory);
        }

        private PathAtom Register(StringTable stringTable, string smallMoniker, string largeMoniker, ref List<PathAtom> versions)
        {
            var pathAtom = PathAtom.Create(stringTable, smallMoniker);
            TargetFrameworkNameToMoniker.Add(largeMoniker, pathAtom);
            WellknownMonikers.Add(pathAtom);
            versions.Add(pathAtom);

            return pathAtom;
        }
    }
}

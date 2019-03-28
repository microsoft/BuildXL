// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// A target framework for NuGet.
    /// </summary>
    /// <remarks>
    /// Contains a framework moniker and the associated target framework folder.
    /// Object equality is solely based on the moniker.
    /// </remarks>
    public readonly struct NugetTargetFramework : IEquatable<NugetTargetFramework>
    {
        /// <nodoc />
        public PathAtom Moniker { get; }

        /// <nodoc />
        public PathAtom TargetFrameworkFolder { get; }

        /// <nodoc />
        public NugetTargetFramework(PathAtom moniker)
            : this(moniker, moniker)
        { }

        /// <nodoc />
        public NugetTargetFramework(PathAtom moniker, PathAtom targetFrameworkFolder)
        {
            Moniker = moniker;
            TargetFrameworkFolder = targetFrameworkFolder;
        }

        /// <nodoc/>
        public bool Equals(NugetTargetFramework other)
        {
            return Moniker == other.Moniker;
        }

        /// <nodoc/>
        public override int GetHashCode()
        {
            return Moniker.GetHashCode();
        }

        /// <nodoc/>
        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return Moniker == ((NugetTargetFramework)obj).Moniker;
        }

        /// <nodoc/>
        public static bool operator ==(NugetTargetFramework x, NugetTargetFramework y)
        {
            return x.Moniker == y.Moniker;
        }

        /// <nodoc/>
        public static bool operator !=(NugetTargetFramework x, NugetTargetFramework y)
        {
            return !(x == y);
        }
    }
}

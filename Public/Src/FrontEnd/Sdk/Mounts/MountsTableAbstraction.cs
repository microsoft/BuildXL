// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Represents access to mount information for the frontends
    /// </summary>
    public abstract class MountsTableAbstraction
    {
        /// <summary>
        /// Returns the list of mount names available in the current package
        /// </summary>
        [NotNull]
        public abstract IEnumerable<string> GetMountNames(ModuleId currentPackage);

        /// <summary>
        /// Tries to get the mount from the mounts table.
        /// </summary>
        /// <remarks>
        /// One has to pass the current package because mounts could be defined on a per package basis.
        /// This method takes care of reporting errors
        /// </remarks>
        public abstract TryGetMountResult TryGetMount(string name, ModuleId currentPackage, out IMount mount);
    }

    /// <summary>
    /// Result type of trying to get a mount
    /// </summary>
    public enum TryGetMountResult
    {
        /// <nodoc />
        Success = 0,

        /// <nodoc />
        NameNullOrEmpty = 1,

        /// <nodoc />
        NameNotFound = 2,
    }
}

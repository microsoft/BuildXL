// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A discriminating union of string, absolute path, relative path or path atom
    /// </summary>
    /// <remarks>
    /// Keep in sync with DScript definition in Public\Sdk\Public\Prelude\Prelude.Configuration.Resolvers.dsc
    /// </remarks>
    public class RushArgument : DiscriminatingUnion
    {
        /// <nodoc/>
        public RushArgument() : base(typeof(string), typeof(AbsolutePath), typeof(RelativePath), typeof(PathAtom))
        { }
    }
}

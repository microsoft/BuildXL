// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Resolvers
{
    /// <summary>
    /// An environment variable can be defined to have a value of the following types:
    /// string, int, <see cref="AbsolutePath"/>, <see cref="PathAtom"/>, <see cref="RelativePath"/>,
    /// <see cref="ICompoundEnvironmentData"/>, <see cref="DirectoryArtifact"/> and <see cref="UnitValue"/>
    /// </summary>
    /// <remarks>
    /// <see cref="UnitValue"/> indicates the variable is a passthrough one. This case is included here for convenience
    /// even though Public\Sdk\Public\Prelude\Prelude.Configuration.Resolvers.dsc defines EnvironmentData without this case.
    /// However, the environment is defined as a map of string to (PassthroughEnvironmentVariable | EnvironmentData), and we currently
    /// don't have good support for unwrapping nested discriminating unions, so this class is the C# backed implementation of 
    /// (PassthroughEnvironmentVariable | EnvironmentData)
    /// </remarks>
    public class EnvironmentData : DiscriminatingUnion
    {
        /// <nodoc/>
        public EnvironmentData() : base(
            typeof(string), 
            typeof(int), 
            typeof(AbsolutePath), 
            typeof(PathAtom), 
            typeof(RelativePath), 
            typeof(ICompoundEnvironmentData), 
            typeof(DirectoryArtifact), 
            typeof(UnitValue)) 
        { }

        /// <nodoc/>
        public EnvironmentData(int i) : this() 
        {
            TrySetValue(i);
        }

        /// <nodoc/>
        public EnvironmentData(string s) : this()
        {
            TrySetValue(s);
        }

        /// <nodoc/>
        public EnvironmentData(AbsolutePath p) : this()
        {
            TrySetValue(p);
        }

        /// <nodoc/>
        public EnvironmentData(PathAtom a) : this()
        {
            TrySetValue(a);
        }

        /// <nodoc/>
        public EnvironmentData(RelativePath r) : this()
        {
            TrySetValue(r);
        }

        /// <nodoc/>
        public EnvironmentData(ICompoundEnvironmentData data) : this()
        {
            TrySetValue(data);
        }

        /// <nodoc/>
        public EnvironmentData(DirectoryArtifact d) : this()
        {
            TrySetValue(d);
        }

        /// <nodoc/>
        public EnvironmentData(UnitValue u) : this()
        {
            TrySetValue(u);
        }
    }
}

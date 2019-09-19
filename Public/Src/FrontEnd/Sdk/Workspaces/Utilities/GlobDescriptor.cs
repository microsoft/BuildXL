// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// A struct representing the various components of a glob/globR call
    /// </summary>
    public readonly struct GlobDescriptor : IEquatable<GlobDescriptor>
    {
        /// <nodoc/>
        [JetBrains.Annotations.NotNull]
        public AbsolutePath Root { get; }

        /// <summary>
        /// The search pattern to use to filter paths under <see cref="Root"/>.
        /// </summary>
        /// <remarks>
        /// This does not support regular expressions. It should be a filesystem pattern --
        /// that is, it may contain a combination of valid literal path and wildcard (* and ?) characters.
        /// </remarks>
        [JetBrains.Annotations.NotNull]
        public string SearchPattern { get; }

        /// <summary>
        /// Whether to recursively search subdirectories (i.e. globR).
        /// </summary>
        public bool Recursive { get; }

        /// <nodoc/>
        public static readonly GlobDescriptor Invalid = new GlobDescriptor(AbsolutePath.Invalid, string.Empty, false);

        /// <nodoc/>
        public GlobDescriptor(AbsolutePath root, string pattern, bool recursive)
        {
            Root = root;
            SearchPattern = pattern;
            Recursive = recursive;
        }

        /// <nodoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, null))
            {
                return false;
            }

            return GetType() == obj.GetType() && Equals((GlobDescriptor)obj);
        }

        /// <nodoc/>
        public bool Equals(GlobDescriptor other)
        {
            return
                Root.Equals(other.Root) &&
                string.Equals(SearchPattern, other.SearchPattern) &&
                Recursive == other.Recursive;
        }

        /// <nodoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Root.GetHashCode(), SearchPattern.GetHashCode(), Recursive.GetHashCode());
        }

        /// <nodoc/>
        public static bool operator ==(GlobDescriptor left, GlobDescriptor right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(GlobDescriptor left, GlobDescriptor right)
        {
            return !left.Equals(right);
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     An path that is guaranteed to be relative.
    /// </summary>
    public class RelativePath : PathBase, IEquatable<RelativePath>
    {
        /// <summary>
        ///     Convenience for the root path.
        /// </summary>
        public static readonly RelativePath RootPath = new RelativePath(string.Empty);

        /// <summary>
        ///     Initializes a new instance of the <see cref="RelativePath" /> class.
        ///     Construct from a simple string.
        /// </summary>
        public RelativePath(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (IsLocalAbsoluteCalculated(path) || IsUncCalculated(path))
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.CurrentCulture, "relative paths cannot be local absolute or UNC paths: {0}", path));
            }

            var segments = ParseSegments(path, true);

            try
            {
                Path = System.IO.Path.Combine(segments.ToArray());
            }
            catch (ArgumentException e) when (IsIllegalCharactersInPathError(e))
            {
                throw CreateIllegalCharactersInPathError(path);
            }
        }

        /// <summary>
        ///     Gets get the parent path.
        /// </summary>
        public RelativePath? Parent
        {
            get
            {
                // Path can't be null, but it can be empty.
                if (string.IsNullOrEmpty(Path))
                {
                    return null;
                }

                string? parent = System.IO.Path.GetDirectoryName(Path);
                if (string.IsNullOrEmpty(parent))
                {
                    return RootPath;
                }

                return new RelativePath(parent);
            }
        }

        /// <inheritdoc />
        public bool Equals(RelativePath other)
        {
            return base.Equals(other);
        }

        /// <summary>
        ///     Concatenate paths.
        /// </summary>
        public static RelativePath operator /(RelativePath left, RelativePath right)
        {
            Contract.RequiresNotNull(left);
            Contract.RequiresNotNull(right);
            Contract.RequiresNotNull(right.Path);
            return left / right.Path;
        }

        /// <summary>
        ///     Concatenate paths.
        /// </summary>
        public static RelativePath operator /(RelativePath left, string right)
        {
            Contract.RequiresNotNull(left);
            Contract.RequiresNotNull(right);

            try
            {
                return new RelativePath(System.IO.Path.Combine(left.Path, right));
            }
            catch (ArgumentException e) when (IsIllegalCharactersInPathError(e))
            {
                throw CreateIllegalCharactersInPathError(right);
            }
        }

        /// <inheritdoc />
        protected override PathBase? GetParentPathBase()
        {
            return Parent;
        }

        /// <inheritdoc />
        protected override PathBase Create<T>(string path)
        {
            return new RelativePath(path);
        }
    }
}

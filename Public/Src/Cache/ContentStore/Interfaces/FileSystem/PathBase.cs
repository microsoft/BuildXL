// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    ///     Encapsulation of FileSystem paths.
    /// </summary>
    public abstract class PathBase : IEquatable<PathBase>
    {
        private const string IllegalCharactersInPathErrorMessage = "Illegal characters in path";
        private static readonly char[] Separators = { '\\', '/' };
        private string _path;

        /// <summary>
        ///     Gets or sets the path as a simple string.
        /// </summary>
        public string Path
        {
            get { return _path; }

            protected set
            {
                Contract.Requires(value != null);
                _path = value;
            }
        }

        /// <summary>
        ///     Gets number of characters in the path.
        /// </summary>
        public virtual int Length => Path.Length;

        /// <summary>
        ///     Gets the filename segment of the path as a simple string.
        /// </summary>
        public string FileName => System.IO.Path.GetFileName(Path);

        /// <inheritdoc />
        public bool Equals(PathBase other)
        {
            if (other == null)
            {
                return false;
            }

            return ReferenceEquals(this, other) || Path.Equals(other.Path, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Check if path is a local one.
        /// </summary>
        protected static bool IsLocalAbsoluteCalculated(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            bool pathCheck = true;

            if (OperatingSystemHelper.IsUnixOS)
            {
                pathCheck = path.StartsWith(@"/", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                pathCheck = path.Substring(1).StartsWith(@":\", StringComparison.OrdinalIgnoreCase) ||
                    path.Substring(1).StartsWith(":/", StringComparison.OrdinalIgnoreCase);
            }


            return pathCheck;
        }

        /// <summary>
        ///     Check if path is UNC.
        /// </summary>
        protected static bool IsUncCalculated(string path)
        {
            Contract.Requires(path != null);
            return path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Return normalized, parsed path segments.
        /// </summary>
        protected static IReadOnlyList<string> ParseSegments(string path, bool relative)
        {
            Contract.Requires(path != null);

            var segmentsRaw = path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            var segments = new List<string>(segmentsRaw.Length);

            foreach (var segment in segmentsRaw)
            {
                if (segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count == 0 && !relative)
                    {
                        throw new ArgumentException("invalid format", nameof(path));
                    }

                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                        continue;
                    }
                }

                segments.Add(segment);
            }

            return segments;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return Equals(obj as PathBase);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Path.ToLower(CultureInfo.CurrentCulture).GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Path;
        }

        /// <summary>
        ///     Concatenates this path with another to produce a new path.
        ///     If you are not sure what the derived type is, do not call this method.
        ///     this is useful if you are producing a generic filesystem that
        ///     can work with absolute or relative paths. If you know the runtime type
        ///     of the path, just use the / operator on the derived type.
        /// </summary>
        public T ConcatenateWith<T>(PathBase relativePath)
            where T : PathBase
        {
            Contract.Requires(relativePath != null);
            return Create<T>(System.IO.Path.Combine(Path, relativePath.Path)) as T;
        }

        /// <summary>
        ///     Concatenates this path with a string to produce a new path.
        ///     If you are not sure what the derived type is, do not call this method.
        ///     this is useful if you are producing a generic filesystem that
        ///     can work with absolute or relative paths. If you know the runtime type
        ///     of the path, just use the / operator on the derived type.
        /// </summary>
        public T ConcatenateWith<T>(string relativePath)
            where T : PathBase
        {
            Contract.Requires(relativePath != null);
            return Create<T>(System.IO.Path.Combine(Path, relativePath)) as T;
        }

        /// <summary>
        ///     Returns the parent, but as a generic of PathBase.
        ///     If you are not sure what the derived type is, use PathBase as the generic type.
        ///     this is useful if you are producing a generic filesystem that
        ///     can work with absolute or relative paths. If you know the runtime type
        ///     of the path, just use the Parent property on the derived type.
        /// </summary>
        public T GetParentPath<T>()
            where T : PathBase
        {
            return GetParentPathBase() as T;
        }

        /// <summary>
        ///     Get path to parent directory.
        /// </summary>
        protected abstract PathBase GetParentPathBase();

        /// <summary>
        ///     Create from a simple string path.
        /// </summary>
        protected abstract PathBase Create<T>(string path);

        /// <summary>
        /// Creates a new path with <paramref name="oldValue"/> replaced with <paramref name="newValue"/>
        /// </summary>
        public T Replace<T>(string oldValue, string newValue)
            where T : PathBase
        {
            return Create<T>(Path.Replace(oldValue, newValue)) as T;
        }

        /// <summary>
        ///     Equality operator.
        /// </summary>
        public static bool operator ==(PathBase left, PathBase right)
        {
            return Equals(left, right);
        }

        /// <summary>
        ///     Inequality operator.
        /// </summary>
        public static bool operator !=(PathBase left, PathBase right)
        {
            return !(left == right);
        }

        /// <nodoc />
        protected static bool IsIllegalCharactersInPathError(ArgumentException e)
        {
            return e.Message.Contains(IllegalCharactersInPathErrorMessage);
        }

        /// <nodoc />
        protected static ArgumentException CreateIllegalCharactersInPathError(string path)
        {
            var message = $"{IllegalCharactersInPathErrorMessage} '{path}'.";
            return new ArgumentException(message);
        }
    }
}

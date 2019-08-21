// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// Result of the GetBulk call
    /// </summary>
    public class GetBulkLocationsResult : BoolResult, IEquatable<GetBulkLocationsResult>
    {
        /// <summary>
        ///     Gets the returned size and locations.
        /// </summary>
        public IReadOnlyList<ContentHashWithSizeAndLocations> ContentHashesInfo { get; }

        /// <summary>
        ///     Gets the number of elements in the result instance.
        /// </summary>
        public int Count => ContentHashesInfo.Count;

        /// <summary>
        ///     Gets the origin of the result.
        /// </summary>
        public GetBulkOrigin Origin { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetBulkLocationsResult"/> class.
        /// </summary>
        public GetBulkLocationsResult(IReadOnlyList<ContentHashWithSizeAndLocations> contentHashesInfo, GetBulkOrigin origin = GetBulkOrigin.Global)
            : base(contentHashesInfo != null)
        {
            ContentHashesInfo = contentHashesInfo;
            Origin = origin;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetBulkLocationsResult"/> class.
        /// </summary>
        public GetBulkLocationsResult(string errorMessage, string diagnostics = null)
            : base(errorMessage, diagnostics)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetBulkLocationsResult"/> class.
        /// </summary>
        public GetBulkLocationsResult(Exception exception, string message = null)
            : base(exception, message)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="GetBulkLocationsResult"/> class.
        /// </summary>
        public GetBulkLocationsResult(ResultBase other, string message = null)
            : base(other, message)
        {
        }

        /// <summary>
        ///     Merges two results together.
        /// </summary>
        /// <remarks>
        /// If <paramref name="right"/> is null, the result is <code>this</code>, otherwise a new instance is constructed with the combined results.
        /// </remarks>
        public GetBulkLocationsResult Merge(GetBulkLocationsResult right)
        {
            return MergeResults(this, right);
        }

        private static GetBulkLocationsResult MergeResults(GetBulkLocationsResult left, GetBulkLocationsResult right)
        {
            Contract.Requires(left != null);

            if (right == null || !left.Succeeded)
            {
                return left;
            }

            if (!right.Succeeded)
            {
                return right;
            }

            var contentHashInfo = new List<ContentHashWithSizeAndLocations>(left.ContentHashesInfo);

            if (left.Count != right.Count)
            {
                Contract.Assert(false, $"Can't merge results of different sizes. left.Count is {left.Count}, right.Count is {right.Count}");
            }

            for (int i = 0; i < contentHashInfo.Count; i++)
            {
                contentHashInfo[i] = Merge(left.ContentHashesInfo[i], right.ContentHashesInfo[i]);
            }

            return new GetBulkLocationsResult(contentHashInfo);
        }

        private static ContentHashWithSizeAndLocations Merge(ContentHashWithSizeAndLocations left, ContentHashWithSizeAndLocations right)
        {
            Contract.Requires(left.ContentHash == right.ContentHash);
            Contract.Requires(left.Size == -1 || right.Size == -1 || right.Size == left.Size);
            var finalList = (left.Locations ?? Enumerable.Empty<MachineLocation>()).Union(right.Locations ?? Enumerable.Empty<MachineLocation>());
            return new ContentHashWithSizeAndLocations(left.ContentHash, Math.Max(left.Size, right.Size), finalList.ToList());
        }

        /// <summary>
        /// Subtracts the given result
        /// </summary>
        public GetBulkLocationsResult Subtract(GetBulkLocationsResult other)
        {
            if (other == null || !other.Succeeded || !Succeeded || ReferenceEquals(this, other))
            {
                return this;
            }

            return SubtractResults(this, other);
        }

        private static GetBulkLocationsResult SubtractResults(GetBulkLocationsResult left, GetBulkLocationsResult right)
        {
            Contract.Requires(left != null);

            if (right == null)
            {
                return left;
            }

            var contentHashInfo = new List<ContentHashWithSizeAndLocations>(left.ContentHashesInfo);

            if (left.Count != right.Count)
            {
                Contract.Assert(false, $"Can't subtract results of different sizes. left.Count is {left.Count}, right.Count is {right.Count}");
            }

            for (int i = 0; i < contentHashInfo.Count; i++)
            {
                contentHashInfo[i] = Subtract(left.ContentHashesInfo[i], right.ContentHashesInfo[i]);
            }

            return new GetBulkLocationsResult(contentHashInfo);
        }

        private static ContentHashWithSizeAndLocations Subtract(ContentHashWithSizeAndLocations left, ContentHashWithSizeAndLocations right)
        {
            Contract.Requires(left.ContentHash == right.ContentHash);
            Contract.Requires(left.Size == -1 || right.Size == -1 || right.Size == left.Size);
            var finalList = (left.Locations ?? Enumerable.Empty<MachineLocation>()).Except(right.Locations ?? Enumerable.Empty<MachineLocation>());
            return new ContentHashWithSizeAndLocations(left.ContentHash, Math.Max(left.Size, right.Size), finalList.ToList());
        }

        /// <summary>
        /// Merges two results together.
        /// </summary>
        public static GetBulkLocationsResult operator +(GetBulkLocationsResult left, GetBulkLocationsResult right)
        {
            return left.Merge(right);
        }

        /// <summary>
        /// Subtracts the given result
        /// </summary>
        public static GetBulkLocationsResult operator -(GetBulkLocationsResult left, GetBulkLocationsResult right)
        {
            return left.Subtract(right);
        }

        /// <inheritdoc />
        public bool Equals(GetBulkLocationsResult other)
        {
            return base.Equals(other) && other != null && ContentHashesInfo.Equals(other.ContentHashesInfo);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is GetBulkLocationsResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ (ContentHashesInfo?.GetHashCode() ?? 0);
        }
    }
}

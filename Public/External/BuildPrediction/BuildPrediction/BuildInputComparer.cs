// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Build.Prediction
{
    /// <summary>
    /// Comparer class for <see cref="BuildInput"/>.
    /// </summary>
    internal sealed class BuildInputComparer : IEqualityComparer<BuildInput>
    {
        public bool Equals(BuildInput x, BuildInput y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.IsDirectory == y.IsDirectory &&
                   x.PredictedBy.Count == y.PredictedBy.Count &&
                   PathComparer.Instance.Equals(x.Path, y.Path) &&
                   x.PredictedBy.All(p => y.PredictedBy.Contains(p));
        }

        public int GetHashCode(BuildInput obj)
        {
            return (obj.IsDirectory ? 0x444444 : 0x888888) ^
                   PathComparer.Instance.GetHashCode(obj.Path) ^
                   (obj.PredictedBy.Count == 0 ? 0xAAAAAA : obj.PredictedBy.Count * 0xBB) ^
                   obj.PredictedBy.Aggregate(0, (current, s) => current ^ s.GetHashCode());
        }
    }
}

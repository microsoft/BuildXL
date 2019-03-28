// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Factory class for constructing <see cref="ValueTask{T}"/>.
    /// </summary>
    public static class ValueTask
    {
        /// <nodoc />
        public static ValueTask<T> FromResult<T>(T value)
        {
            return new ValueTask<T>(value);
        }
    }
}

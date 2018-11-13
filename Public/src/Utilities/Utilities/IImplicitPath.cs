// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents an object that is implicitly convertible to an absolute path.
    /// </summary>
    public interface IImplicitPath
    {
        /// <summary>
        /// Gets the absolute path representation of the object.
        /// </summary>
        AbsolutePath Path { get; }
    }
}

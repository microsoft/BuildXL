// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Mode for placing or ingressing files to an <see cref="IArtifactContentCache"/>.
    /// </summary>
    public enum FileRealizationMode
    {
        /// <summary>
        /// Always copy. Upon completion, the file will be writable.
        /// </summary>
        Copy,

        /// <summary>
        /// Always hardlink. Upon completion, the file will be read-only.
        /// </summary>
        HardLink,

        /// <summary>
        /// Prefer hardlinking, but fall back to copy. Assume that the file is read-only upon completion.
        /// </summary>
        HardLinkOrCopy,
    }
}

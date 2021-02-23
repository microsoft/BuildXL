// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Pips.Operations
{
    /// <nodoc/>
    public static class SealDirectoryCompositionActionKindExtensions
    {
        /// <summary>
        /// Whether an action describes a composite shared opaque seal directory
        /// </summary>
        public static bool IsComposite(this SealDirectoryCompositionActionKind actionKind) => actionKind != SealDirectoryCompositionActionKind.None;
    }
}

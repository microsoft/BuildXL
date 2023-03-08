// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;

namespace TypeScript.Net.Incrementality
{
    /// <summary>
    /// Binding information of the source file used by incremental scenarios.
    /// </summary>
    public interface ISourceFileBindingState
    {
        /// <summary>
        /// Full path to the spec.
        /// </summary>
        /// TODO: replace with a property once AbsolutePath is properly introduced in ISourceFile
        AbsolutePath GetAbsolutePath(PathTable pathTable);

        /// <summary>
        /// Bit vector that represents files that the current file depend on.
        /// If the bit is set then the current file depends on file with a given index.
        /// </summary>
        [AllowNull]
        RoaringBitSet FileDependents { get; }

        /// <summary>
        /// Set of files that depend on the current file.
        /// If the bit is set then the current file depends on file with a given index.
        /// </summary>
        [AllowNull]
        RoaringBitSet FileDependencies { get; }

        /// <summary>
        /// Returns binding symbols for the current file.
        /// </summary>
        [AllowNull]
        ISpecBindingSymbols BindingSymbols { get; }
    }
}

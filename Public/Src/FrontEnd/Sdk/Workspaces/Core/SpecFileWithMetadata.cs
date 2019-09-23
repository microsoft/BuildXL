// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <nodoc />
    [DebuggerDisplay("ModuleDescriptor = {ToString(), nq}")]
    public readonly struct SpecFileWithMetadata : IEquatable<SpecFileWithMetadata>
    {
        /// <nodoc />
        [NotNull]
        public ISourceFile SourceFile { get; }

        /// <nodoc />
        [NotNull]
        public ParsedModule OwningModule { get; }

        /// <nodoc />
        public SpecState State { get; }

        /// <nodoc />
        public SpecFileWithMetadata([NotNull]ISourceFile sourceFile, [NotNull]ParsedModule owningModule, SpecState state)
        {
            Contract.Requires(sourceFile != null);
            Contract.Requires(owningModule != null);

            SourceFile = sourceFile;
            OwningModule = owningModule;
            State = state;
        }

        /// <nodoc />
        public static SpecFileWithMetadata CreateNew(ParsedModule owningModule, ISourceFile sourceFile)
        {
            return new SpecFileWithMetadata(sourceFile, owningModule, SpecState.Changed);
        }

        /// <nodoc />
        internal SpecFileWithMetadata WithSpecState(SpecState state)
        {
            return new SpecFileWithMetadata(SourceFile, OwningModule, state);
        }

        /// <inheritdoc/>
        public bool Equals(SpecFileWithMetadata other)
        {
            return Equals(SourceFile, other.SourceFile) && Equals(OwningModule, other.OwningModule) && State == other.State;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return I($"Module: {OwningModule.Descriptor.Name}, Spec: {SourceFile.Path.AbsolutePath}");
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is SpecFileWithMetadata && Equals((SpecFileWithMetadata)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                SourceFile.GetHashCode(),
                OwningModule.GetHashCode(),
                EqualityComparer<SpecState>.Default.GetHashCode(State));
        }

        /// <nodoc />
        public static bool operator ==(SpecFileWithMetadata left, SpecFileWithMetadata right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(SpecFileWithMetadata left, SpecFileWithMetadata right)
        {
            return !left.Equals(right);
        }
    }
}

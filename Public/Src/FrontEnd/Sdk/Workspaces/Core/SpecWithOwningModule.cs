// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// A path to a spec together with the descriptor of its owning module.
    /// </summary>
    public readonly struct SpecWithOwningModule : IEquatable<SpecWithOwningModule>
    {
        /// <nodoc />
        public SpecWithOwningModule(AbsolutePath path, [CanBeNull]ModuleDefinition owningModule)
        {
            Contract.Requires(path.IsValid, "path.IsValid");

            Path = path;
            OwningModule = owningModule;
        }

        /// <nodoc />
        public AbsolutePath Path { get; }

        /// <nodoc />
        [CanBeNull]
        public ModuleDefinition OwningModule { get; }

        /// <nodoc />
        public ModuleDescriptor OwningModuleDescriptor
        {
            get
            {
                Contract.Requires(OwningModule != null, "To provide module definition, OwningModule should be provided during construction.");
                return OwningModule.Descriptor;
            }
        }

        /// <inheritdoc/>
        public bool Equals(SpecWithOwningModule other)
        {
            return Path.Equals(other.Path) && OwningModule?.Equals(other.OwningModule) == true;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is SpecWithOwningModule && Equals((SpecWithOwningModule)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Path.GetHashCode(), OwningModule?.GetHashCode() ?? 42);
        }

        /// <nodoc />
        public static bool operator ==(SpecWithOwningModule left, SpecWithOwningModule right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(SpecWithOwningModule left, SpecWithOwningModule right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// A parsed spec together with the descriptor of its owning module.
    /// </summary>
    public readonly struct ParsedSpecWithOwningModule : IEquatable<ParsedSpecWithOwningModule>
    {
        /// <nodoc />
        public ParsedSpecWithOwningModule(ISourceFile parsedFile, ModuleDefinition owningModule)
        {
            Contract.Requires(owningModule != null, "owningModule != null");
            Contract.Requires(parsedFile != null, "parsedFile != null");

            OwningModule = owningModule;
            ParsedFile = parsedFile;
        }

        /// <nodoc />
        public ModuleDefinition OwningModule { get; }

        /// <nodoc />
        public ISourceFile ParsedFile { get; }

        /// <inheritdoc/>
        public bool Equals(ParsedSpecWithOwningModule other)
        {
            return OwningModule.Equals(other.OwningModule) && ParsedFile == other.ParsedFile;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is ParsedSpecWithOwningModule && Equals((ParsedSpecWithOwningModule)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(OwningModule.GetHashCode(), ParsedFile.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(ParsedSpecWithOwningModule left, ParsedSpecWithOwningModule right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ParsedSpecWithOwningModule left, ParsedSpecWithOwningModule right)
        {
            return !left.Equals(right);
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Context for errors.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ErrorContext
    {
        /// <summary>
        /// Argument position or position in an array literal if array literal (see <see cref="ObjectCtx"/>) is non-null.
        /// </summary>
        public int Pos { get; }

        /// <summary>
        /// Field name in an object literal if object literal (see <see cref="ObjectCtx"/>) is non-null.
        /// </summary>
        public StringId Name { get; }

        /// <summary>
        /// Object context, which can either be array literal or object literal.
        /// </summary>
        public object ObjectCtx { get; }

        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames")]
        public ErrorContext(int pos = -1, SymbolAtom name = default(SymbolAtom), object objectCtx = null)
        {
            Pos = pos;
            Name = name.StringId;
            ObjectCtx = objectCtx;
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames")]
        public ErrorContext(StringId name, object objectCtx)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(objectCtx != null);

            Pos = 0;
            Name = name;
            ObjectCtx = objectCtx;
        }

        private ErrorContext(int pos, StringId name, object objectCtx)
        {
            Pos = pos;
            Name = name;
            ObjectCtx = objectCtx;
        }

        /// <summary>
        /// Creates a copy with a new pos.
        /// </summary>
        public ErrorContext WithNewPos(int pos)
        {
            return new ErrorContext(pos, Name, ObjectCtx);
        }

        /// <summary>
        /// Creates a copy with a new name.
        /// </summary>
        public ErrorContext WithNewName(SymbolAtom name)
        {
            return WithNewName(name.StringId);
        }

        /// <summary>
        /// Creates a copy with a new name.
        /// </summary>
        public ErrorContext WithNewName(StringId name)
        {
            return new ErrorContext(Pos, name, ObjectCtx);
        }

        /// <summary>
        /// Returns string representation of the error.
        /// </summary>
        public string ToErrorString(ImmutableContextBase context)
        {
            return new DisplayStringHelper(context).ToErrorString(this);
        }

        /// <summary>
        /// Returns string representation of the first part of an error.
        /// </summary>
        public string ErrorReceiverAsString(ImmutableContextBase context)
        {
            return new DisplayStringHelper(context).ErrorReceiverAsString(this);
        }
    }
}

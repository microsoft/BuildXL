// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Context for conversion.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ConversionContext
    {
        /// <summary>
        /// Allow undefined value during the conversion.
        /// </summary>
        /// <remarks>
        /// For value type, the undefined will be converted to the default value, while
        /// for reference type, the undefined will be converted to null.
        /// </remarks>
        public bool AllowUndefined { get; }

        /// <summary>
        /// General error context.
        /// </summary>
        public ErrorContext ErrorContext { get; }

        /// <nodoc/>
        public int Pos => ErrorContext.Pos;

        /// <nodoc/>
        public StringId Name => ErrorContext.Name;

        /// <nodoc/>
        public object ObjectCtx => ErrorContext.ObjectCtx;

        /// <nodoc />
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames")]
        public ConversionContext(bool allowUndefined = false, int pos = -1, SymbolAtom name = default(SymbolAtom), object objectCtx = null)
        {
            AllowUndefined = allowUndefined;
            ErrorContext = new ErrorContext(pos, name, objectCtx);
        }

        private ConversionContext(bool allowUndefined, ErrorContext errorContext)
        {
            AllowUndefined = allowUndefined;
            ErrorContext = errorContext;
        }

        /// <summary>
        /// Creates with a new pos.
        /// </summary>
        public ConversionContext WithNewPos(int pos)
        {
            return new ConversionContext(AllowUndefined, ErrorContext.WithNewPos(pos));
        }

        /// <summary>
        /// Creates with a new name.
        /// </summary>
        public ConversionContext WithNewName(SymbolAtom name)
        {
            return new ConversionContext(AllowUndefined, ErrorContext.WithNewName(name));
        }

        /// <summary>
        /// Creates with a new string name.
        /// </summary>
        public ConversionContext WithNewName(StringId name)
        {
            return new ConversionContext(AllowUndefined, ErrorContext.WithNewName(name));
        }
    }
}

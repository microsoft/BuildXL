// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Bindings for object literal.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    [SuppressMessage("Microsoft.Design", "CA1036:OverrideMethodsOnComparableTypes")]
    public readonly struct Binding : IComparable<Binding>
    {
        /// <summary>
        /// Name.
        /// </summary>
        public StringId Name { get; }

        /// <summary>
        /// Expression.
        /// </summary>
        public object Body { get; }

        /// <summary>
        /// Location.
        /// </summary>
        public LineInfo Location { get; }

        /// <nodoc />
        public Binding(SymbolAtom name, EvaluationResult body, LineInfo location)
            : this(name.StringId, body.Value, location)
        {
            Contract.Requires(name.IsValid);
        }
        
        /// <nodoc />
        public Binding(SymbolAtom name, object body, LineInfo location)
            : this(name.StringId, body, location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(!(body is EvaluationResult));
        }

        /// <nodoc />
        public Binding(StringId name, object body, LineInfo location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(!(body is EvaluationResult));

            Name = name;
            Body = body;
            Location = location;
        }
        
        /// <nodoc />
        public Binding(StringId name, EvaluationResult body, LineInfo location)
        : this(name, body.Value, location)
        {
        }

        /// <inheritdoc />
        public int CompareTo(Binding other)
        {
            return Name.Value.CompareTo(other.Name.Value);
        }
    }
}

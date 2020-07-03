// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Undefined value.
    /// </summary>
    [DebuggerDisplay("undefined")]
    public sealed class UndefinedValue
    {
        /// <summary>
        /// Instance of undefined value.
        /// </summary>
        public static UndefinedValue Instance { get; } = new UndefinedValue();

        /// <nodoc />
        private UndefinedValue()
        { }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            var undefinedValue = obj as UndefinedValue;
            return undefinedValue != null && Equals(undefinedValue);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // This value is singleton. Can just return a constant.
            return 42;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return "undefined";
        }
    }
}

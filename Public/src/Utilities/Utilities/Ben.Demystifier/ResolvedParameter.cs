// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;

namespace System.Diagnostics
{
    /// <nodoc />
    public class ResolvedParameter
    {
        /// <nodoc />
        public string Name { get; set; }

        /// <nodoc />
        public string Type { get; set; }

        /// <nodoc />
        public Type ResolvedType { get; set; }

        /// <nodoc />
        public string Prefix { get; set; }

        /// <inheritdoc />
        public override string ToString() => Append(new StringBuilder()).ToString();

        internal StringBuilder Append(StringBuilder sb)
        {
            if (!string.IsNullOrEmpty(Prefix))
            {
                sb.Append(Prefix)
                  .Append(" ");
            }

            sb.Append(Type);
            if (!string.IsNullOrEmpty(Name))
            {
                sb.Append(" ")
                  .Append(Name);
            }

            return sb;
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic.Enumerable;
using System.Reflection;
using System.Text;

namespace System.Diagnostics
{
    /// <nodoc />
    public class ResolvedMethod
    {
        /// <nodoc />
        public MethodBase MethodBase { get; set; }

        /// <nodoc />
        public string DeclaringTypeName { get; set; }

        /// <nodoc />
        public bool IsAsync { get; set; }

        /// <nodoc />
        public bool IsLambda { get; set; }

        /// <nodoc />
        public ResolvedParameter ReturnParameter { get; set; }

        /// <nodoc />
        public string Name { get; set; }

        /// <nodoc />
        public int? Ordinal { get; set; }

        /// <nodoc />
        public string GenericArguments { get; set; }

        /// <nodoc />
        public Type[] ResolvedGenericArguments { get; set; }

        /// <nodoc />
        public MethodBase SubMethodBase { get; set; }

        /// <nodoc />
        public string SubMethod { get; set; }

        /// <nodoc />
        public EnumerableIList<ResolvedParameter> Parameters { get; set; }

        /// <nodoc />
        public EnumerableIList<ResolvedParameter> SubMethodParameters { get; set; }

        /// <inheritdoc />
        public override string ToString() => Append(new StringBuilder()).ToString();

        internal StringBuilder Append(StringBuilder builder)
        {

            if (IsAsync)
            {
                builder
                    .Append("async ");
            }

            if (ReturnParameter != null)
            {
                ReturnParameter.Append(builder);
                builder.Append(" ");
            }

            if (!string.IsNullOrEmpty(DeclaringTypeName))
            {

                if (Name == ".ctor")
                {
                    if (string.IsNullOrEmpty(SubMethod) && !IsLambda)
                        builder.Append("new ");

                    builder.Append(DeclaringTypeName);
                }
                else if (Name == ".cctor")
                {
                    builder.Append("static ");
                    builder.Append(DeclaringTypeName);
                }
                else
                {
                    builder
                        .Append(DeclaringTypeName)
                        .Append(".")
                        .Append(Name);
                }
            }
            else
            {
                builder.Append(Name);
            }
            builder.Append(GenericArguments);

            builder.Append("(");
            if (MethodBase != null)
            {
                var isFirst = true;
                foreach(var param in Parameters)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        builder.Append(", ");
                    }
                    param.Append(builder);
                }
            }
            else
            {
                builder.Append("?");
            }
            builder.Append(")");

            if (!string.IsNullOrEmpty(SubMethod) || IsLambda)
            {
                builder.Append("+");
                builder.Append(SubMethod);
                builder.Append("(");
                if (SubMethodBase != null)
                {
                    var isFirst = true;
                    foreach (var param in SubMethodParameters)
                    {
                        if (isFirst)
                        {
                            isFirst = false;
                        }
                        else
                        {
                            builder.Append(", ");
                        }
                        param.Append(builder);
                    }
                }
                else
                {
                    builder.Append("?");
                }
                builder.Append(")");
                if (IsLambda)
                {
                    builder.Append(" => { }");

                    if (Ordinal.HasValue)
                    {
                        builder.Append(" [");
                        builder.Append(Ordinal);
                        builder.Append("]");
                    }
                }
            }

            return builder;
        }
    }
}

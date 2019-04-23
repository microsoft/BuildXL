// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for <code>namespace StringBuilder {}</code> and <code>interface StringBuilder {}</code>.
    /// </summary>
    public sealed class AmbientStringBuilder : AmbientDefinition<AmbientStringBuilder.StringBuilderWrapper>
    {
        /// <nodoc />
        public AmbientStringBuilder(PrimitiveTypes knownTypes)
            : base("StringBuilder", knownTypes)
        {
        }

        private CallSignature CreateMethodSignature => CreateSignature(
            returnType: AmbientTypes.StringBuilderType);

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "StringBuilder",
                new[]
                {
                    Function("create", Create, CreateMethodSignature),
                });
        }

        private static EvaluationResult Create(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var stringBuilderWrapper = Pools.StringBuilderPool.GetInstance();

            return EvaluationResult.Create(new StringBuilderWrapper(stringBuilderWrapper));
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<StringBuilderWrapper>> CreateMembers()
        {
            return new[]
            {
                Create<StringBuilderWrapper>(AmbientName, Symbol("append"), Append),
                Create<StringBuilderWrapper>(AmbientName, Symbol("appendLine"), AppendLine),
                Create<StringBuilderWrapper>(AmbientName, Symbol("appendRepeat"), AppendRepeat),
                Create<StringBuilderWrapper>(AmbientName, Symbol("replace"), Replace),

            }.ToDictionary(m => m.Name.StringId, m => m);
        }

        /// <inheritdoc />
        protected override CallableMember<StringBuilderWrapper> CreateToStringMember()
        {
            return Create<StringBuilderWrapper>(AmbientName, Symbol("toString"), ToStringAndRelease);
        }

        private static EvaluationResult Append(Context context, StringBuilderWrapper receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var stringValue = Converter.ExpectString(arg);

            receiver.StringBuilder.Append(stringValue);

            return EvaluationResult.Create(receiver);
        }

        private static EvaluationResult AppendLine(Context context, StringBuilderWrapper receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var stringValue = Converter.ExpectString(arg, new ConversionContext(allowUndefined: true));

            if (stringValue != null)
            {
                receiver.StringBuilder.Append(stringValue);
            }

            receiver.StringBuilder.Append('\n');

            return EvaluationResult.Create(receiver);
        }

        private static EvaluationResult AppendRepeat(Context context, StringBuilderWrapper receiver, EvaluationResult arg1, EvaluationResult arg2, EvaluationStackFrame captures)
        {
            var stringValue = Converter.ExpectString(arg1);
            var repeatValue = Math.Max(0, Converter.ExpectNumber(arg2));

            var builder = receiver.StringBuilder;

            if (stringValue.Length == 1)
            {
                builder.Append(stringValue[0], repeatValue);
            }
            else
            {
                for (var i = 0; i < repeatValue; i++)
                {
                    builder.Append(stringValue);
                }
            }

            return EvaluationResult.Create(receiver);
        }

        private static EvaluationResult Replace(Context context, StringBuilderWrapper receiver, EvaluationResult arg1, EvaluationResult arg2, EvaluationStackFrame captures)
        {
            var oldValue = Converter.ExpectString(arg1);
            var newValue = Converter.ExpectString(arg2);

            receiver.StringBuilder.Replace(oldValue, newValue);

            return EvaluationResult.Create(receiver);
        }

        private static EvaluationResult ToStringAndRelease(Context context, StringBuilderWrapper receiver,EvaluationStackFrame captures)
        {
            var result = receiver.StringBuilder.ToString();

            receiver.PooledWrapper.Dispose();

            return EvaluationResult.Create(result);
        }

        /// <summary>
        /// Wrapper around a pooled <see cref="StringBuilder"/> instance.
        /// </summary>
        public class StringBuilderWrapper
        {
            /// <nodoc />
            public StringBuilder StringBuilder => PooledWrapper.Instance;

            /// <nodoc />
            public PooledObjectWrapper<StringBuilder> PooledWrapper { get; }

            /// <nodoc />
            public StringBuilderWrapper(PooledObjectWrapper<StringBuilder> pooledWrapper)
            {
                PooledWrapper = pooledWrapper;
            }
        }
    }
}

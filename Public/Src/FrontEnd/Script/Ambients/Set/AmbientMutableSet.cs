// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients.Set
{
    /// <summary>
    /// Ambient definition for <code>namespace MutableSet {}</code> and <code>interface MutableSet&lt;T&gt; {}</code>
    /// </summary>
    /// <remarks>
    /// This is unsafe code allowed only in perf-critical scenarios and only as implementation details for pure methods.
    /// </remarks>
    public sealed class AmbientMutableSet : AmbientDefinition<MutableSet>
    {
        /// <nodoc />
        public AmbientMutableSet(PrimitiveTypes knownTypes)
            : base("MutableSet", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "MutableSet",
                new[]
                {
                    Function("empty", Empty, EmptySignature),
                });
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<MutableSet>> CreateMembers()
        {
            return new[]
            {
                Create<MutableSet>(AmbientName, Symbol("add"), Add, rest: true),
                Create<MutableSet>(AmbientName, Symbol("remove"), Remove, rest: true),
                Create<MutableSet>(AmbientName, Symbol("contains"), Contains),
                Create<MutableSet>(AmbientName, Symbol("union"), Union),
                Create<MutableSet>(AmbientName, Symbol("toArray"), ToArray),
                Create<MutableSet>(AmbientName, Symbol("count"), Count),
            }.ToDictionary(m => m.Name.StringId, m => m);
        }

        private static EvaluationResult Count(Context context, MutableSet receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Count);
        }

        private static EvaluationResult Union(Context context, MutableSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var set = Converter.ExpectMutableSet(arg, new ConversionContext(pos: 1));
            return EvaluationResult.Create(receiver.Union(set));
        }

        private static EvaluationResult Remove(Context context, MutableSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var argArray = Converter.ExpectArrayLiteral(arg, new ConversionContext(pos: 1));

            for (int i = 0; i < argArray.Length; ++i)
            {
                if (argArray[i].IsUndefined)
                {
                    throw new UndefinedSetItemException(new ErrorContext(objectCtx: argArray, pos: i));
                }
            }

            return EvaluationResult.Create(receiver.RemoveRange(argArray.Values));
        }

        private static EvaluationResult Add(Context context, MutableSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var argArray = Converter.ExpectArrayLiteral(arg, new ConversionContext(pos: 1));

            for (int i = 0; i < argArray.Length; ++i)
            {
                EvaluationResult elem = argArray[i];

                if (elem.IsUndefined)
                {
                    throw new UndefinedSetItemException(new ErrorContext(objectCtx: argArray, pos: i));
                }
            }

            return EvaluationResult.Create(receiver.AddRange(argArray.Values));
        }

        private static EvaluationResult ToArray(Context context, MutableSet receiver, EvaluationStackFrame captures)
        {
            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(receiver.ToArray(), entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Contains(Context context, MutableSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            if (arg.IsUndefined)
            {
                throw new UndefinedSetItemException(new ErrorContext(pos: 1));
            }

            return EvaluationResult.Create(receiver.Contains(arg));
        }

        private static EvaluationResult Empty(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(MutableSet.CreateEmpty());
        }

        private CallSignature EmptySignature => CreateSignature(
            returnType: AmbientTypes.SetType);
    }
}

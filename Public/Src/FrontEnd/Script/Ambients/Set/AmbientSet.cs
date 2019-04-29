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
    /// Ambient definition for <code>namespace Set {}</code> and <code>interface Set&lt;T&gt; {}</code>
    /// </summary>
    public sealed class AmbientSet : AmbientDefinition<OrderedSet>
    {
        /// <nodoc />
        public AmbientSet(PrimitiveTypes knownTypes)
            : base("Set", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "Set",
                new[]
                {
                    Function("empty", Empty, EmptySignature),
                    Function("create", Create, CreateSetSignature)
                });
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<OrderedSet>> CreateMembers()
        {
            return new[]
            {
                Create<OrderedSet>(AmbientName, Symbol("add"), Add, rest: true),
                Create<OrderedSet>(AmbientName, Symbol("contains"), Contains),
                Create<OrderedSet>(AmbientName, Symbol("remove"), Remove, rest: true),
                Create<OrderedSet>(AmbientName, Symbol("union"), Union),
                Create<OrderedSet>(AmbientName, Symbol("intersect"), Intersect),
                Create<OrderedSet>(AmbientName, Symbol("except"), Except),
                Create<OrderedSet>(AmbientName, Symbol("isSubsetOf"), IsSubsetOf),
                Create<OrderedSet>(AmbientName, Symbol("isProperSubsetOf"), IsProperSubsetOf),
                Create<OrderedSet>(AmbientName, Symbol("isSupersetOf"), IsSupersetOf),
                Create<OrderedSet>(AmbientName, Symbol("isProperSupersetOf"), IsProperSupersetOf),
                Create<OrderedSet>(AmbientName, Symbol("toArray"), ToArray),
                Create<OrderedSet>(AmbientName, Symbol("forEach"), ForEach),
                Create<OrderedSet>(AmbientName, Symbol("count"), Count),
            }.ToDictionary(m => m.Name.StringId, m => m);
        }

        private static EvaluationResult Count(Context context, OrderedSet receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Count);
        }

        private static EvaluationResult ForEach(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var cls = Converter.ExpectClosure(arg);
            var result = new EvaluationResult[receiver.Count];
            int paramsCount = cls.Function.Params;

            using (var frame = EvaluationStackFrame.Create(cls.Function, captures.Frame))
            {
                int i = 0;

                foreach (var item in receiver)
                {
                    frame.TrySetArguments(paramsCount, item);
                    result[i] = context.InvokeClosure(cls, frame);

                    if (result[i].IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }

                    ++i;
                }

                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, context.TopStack.InvocationLocation, context.TopStack.Path));
            }
        }

        private static EvaluationResult Union(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var set = Converter.ExpectSet(arg, new ConversionContext(pos: 1));
            return EvaluationResult.Create(receiver.Union(set));
        }

        private static EvaluationResult Intersect(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var set = Converter.ExpectSet(arg, new ConversionContext(pos: 1));
            return EvaluationResult.Create(receiver.Intersect(set));
        }

        private static EvaluationResult Except(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var set = Converter.ExpectSet(arg, new ConversionContext(pos: 1));
            return EvaluationResult.Create(receiver.Except(set));
        }

        private static EvaluationResult IsSubsetOf(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var set = Converter.ExpectSet(arg, new ConversionContext(pos: 1));
            return EvaluationResult.Create(receiver.IsSubsetOf(set));
        }

        private static EvaluationResult IsProperSubsetOf(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var set = Converter.ExpectSet(arg, new ConversionContext(pos: 1));
            return EvaluationResult.Create(receiver.IsProperSubsetOf(set));
        }

        private static EvaluationResult IsSupersetOf(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var set = Converter.ExpectSet(arg, new ConversionContext(pos: 1));
            return EvaluationResult.Create(receiver.IsSupersetOf(set));
        }

        private static EvaluationResult IsProperSupersetOf(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var set = Converter.ExpectSet(arg, new ConversionContext(pos: 1));
            return EvaluationResult.Create(receiver.IsProperSupersetOf(set));
        }

        private static EvaluationResult Remove(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
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

        private static EvaluationResult Add(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
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

        private static EvaluationResult ToArray(Context context, OrderedSet receiver, EvaluationStackFrame captures)
        {
            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(receiver.ToArray(), entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Contains(Context context, OrderedSet receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            if (arg.IsUndefined)
            {
                throw new UndefinedSetItemException(new ErrorContext(pos: 1));
            }

            return EvaluationResult.Create(receiver.Contains(arg));
        }

        private static EvaluationResult Empty(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(OrderedSet.Empty);
        }

        private static EvaluationResult Create(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return Add(context, OrderedSet.Empty, args[0], args);
        }
        
        private CallSignature EmptySignature => CreateSignature(
            returnType: AmbientTypes.SetType);

        private CallSignature CreateSetSignature => CreateSignature(
            restParameterType: AmbientTypes.ObjectType,
            returnType: AmbientTypes.SetType);
    }
}

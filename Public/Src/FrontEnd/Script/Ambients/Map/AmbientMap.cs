// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Ambients.Map
{
    /// <summary>
    /// Ambient definition for <code>namespace Map {}</code> and <code>interface Set&lt;TKey, TValue&gt; {}</code>
    /// </summary>
    public sealed class AmbientMap : AmbientDefinition<OrderedMap>
    {
        /// <nodoc />
        public AmbientMap(PrimitiveTypes knownTypes)
            : base("Map", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "Map",
                new[]
                {
                    Function("empty", Empty, EmptySignature),
                    Function("emptyCaseInsensitive", EmptyCaseInsensitive, EmptyCaseInsensitiveSignature),
                });
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<OrderedMap>> CreateMembers()
        {
            return new[]
            {
                Create<OrderedMap>(AmbientName, Symbol("add"), Add),
                Create<OrderedMap>(AmbientName, Symbol("addRange"), AddRange, rest: true),
                Create<OrderedMap>(AmbientName, Symbol("containsKey"), ContainsKey),
                Create<OrderedMap>(AmbientName, Symbol("get"), Get),
                Create<OrderedMap>(AmbientName, Symbol("remove"), Remove),
                Create<OrderedMap>(AmbientName, Symbol("removeRange"), RemoveRange, rest: true),
                Create<OrderedMap>(AmbientName, Symbol("toArray"), ToArray),
                Create<OrderedMap>(AmbientName, Symbol("forEach"), ForEach),
                Create<OrderedMap>(AmbientName, Symbol("keys"), Keys),
                Create<OrderedMap>(AmbientName, Symbol("values"), Values),
                Create<OrderedMap>(AmbientName, Symbol("count"), Count),
            }.ToDictionary(m => m.Name.StringId, m => m);
        }

        private static EvaluationResult Count(Context context, OrderedMap receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Count);
        }

        private static EvaluationResult Keys(Context context, OrderedMap receiver, EvaluationStackFrame captures)
        {
            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(receiver.Keys(), entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Values(Context context, OrderedMap receiver, EvaluationStackFrame captures)
        {
            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(receiver.Values(), entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult ForEach(Context context, OrderedMap receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var cls = Converter.ExpectClosure(arg);
            var result = new EvaluationResult[receiver.Count];
            int paramsCount = cls.Function.Params;

            // One frame can be reused for multiple calls of a callback function
            using (var frame = EvaluationStackFrame.Create(cls.Function, captures.Frame))
            {
                var entry = context.TopStack;

                int i = 0;

                foreach (var kvp in receiver)
                {
                    var kvpAsArray = ArrayLiteral.CreateWithoutCopy(new EvaluationResult[] { kvp.Key, kvp.Value }, entry.InvocationLocation, entry.Path);

                    frame.TrySetArguments(paramsCount, EvaluationResult.Create(kvpAsArray));
                    result[i] = context.InvokeClosure(cls, frame);

                    if (result[i].IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }

                    ++i;
                }

                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, entry.InvocationLocation, entry.Path));
            }
        }

        private static EvaluationResult RemoveRange(Context context, OrderedMap receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var argArray = Converter.ExpectArrayLiteral(arg, new ConversionContext(pos: 1));

            for (int i = 0; i < argArray.Length; ++i)
            {
                if (argArray[i].IsUndefined)
                {
                    throw new UndefinedMapKeyException(new ErrorContext(objectCtx: argArray, pos: i));
                }
            }

            return EvaluationResult.Create(receiver.RemoveRange(argArray.Values));
        }

        private static EvaluationResult AddRange(Context context, OrderedMap receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.AddRange(ToKeyValuePairs(arg, pos: 1)));
        }

        private static EvaluationResult ToArray(Context context, OrderedMap receiver, EvaluationStackFrame captures)
        {
            var result = new EvaluationResult[receiver.Count];

            var entry = context.TopStack;

            var kvps = receiver.ToArray();
            int i = 0;

            foreach (var kvp in kvps)
            {
                var pair = ArrayLiteral.CreateWithoutCopy(new[] { kvp.Key, kvp.Value }, entry.InvocationLocation, entry.Path);
                result[i] = EvaluationResult.Create(pair);
                ++i;
            }

            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Remove(Context context, OrderedMap receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            if (arg.IsUndefined)
            {
                throw new UndefinedMapKeyException(new ErrorContext(pos: 1));
            }

            return EvaluationResult.Create(receiver.Remove(arg));
        }

        private static EvaluationResult Get(Context context, OrderedMap receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            if (arg.IsUndefined)
            {
                throw new UndefinedMapKeyException(new ErrorContext(pos: 1));
            }

            return receiver.TryGetValue(arg, out EvaluationResult value) ? value : EvaluationResult.Undefined;
        }

        private static EvaluationResult ContainsKey(Context context, OrderedMap receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            if (arg.IsUndefined)
            {
                throw new UndefinedMapKeyException(new ErrorContext(pos: 1));
            }

            return EvaluationResult.Create(receiver.ContainsKey(arg));
        }

        private static EvaluationResult Add(Context context, OrderedMap receiver, EvaluationResult arg1, EvaluationResult arg2, EvaluationStackFrame captures)
        {
            if (arg1.IsUndefined)
            {
                throw new UndefinedMapKeyException(new ErrorContext(pos: 1));
            }

            return EvaluationResult.Create(receiver.Add(arg1, arg2));
        }

        private static KeyValuePair<EvaluationResult, EvaluationResult>[] ToKeyValuePairs(EvaluationResult arg, int pos)
        {
            var argArray = Converter.ExpectArrayLiteral(arg, new ConversionContext(pos: pos));
            var range = new KeyValuePair<EvaluationResult, EvaluationResult>[argArray.Length];

            for (int i = 0; i < argArray.Length; ++i)
            {
                var kvp = Converter.ExpectArrayLiteral(argArray[i], new ConversionContext(objectCtx: argArray, pos: i));

                if (kvp.Length != 2)
                {
                    throw new InvalidKeyValueMapException(new ErrorContext(objectCtx: argArray, pos: i));
                }

                if (kvp[0].IsUndefined)
                {
                    throw new UndefinedMapKeyException(new ErrorContext(objectCtx: argArray, pos: i));
                }

                range[i] = new KeyValuePair<EvaluationResult, EvaluationResult>(kvp[0], kvp[1]);
            }

            return range;
        }

        private static EvaluationResult Empty(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(OrderedMap.Empty);
        }

        private CallSignature EmptySignature => CreateSignature(
            returnType: AmbientTypes.MapType);

        private static EvaluationResult EmptyCaseInsensitive(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            return EvaluationResult.Create(OrderedMap.EmptyCaseInsensitive);
        }

        private CallSignature EmptyCaseInsensitiveSignature => CreateSignature(
            returnType: AmbientTypes.MapType);
    }
}

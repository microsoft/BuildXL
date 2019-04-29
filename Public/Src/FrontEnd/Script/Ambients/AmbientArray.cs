// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.FrontEnd.Script.Statements;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for Array interface.
    /// </summary>
    public sealed class AmbientArray : AmbientDefinition<ArrayLiteral>
    {
        /// <nodoc />
        public AmbientArray(PrimitiveTypes knownTypes)
            : base("Array", knownTypes)
        {
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<ArrayLiteral>> CreateMembers()
        {
            // ambient array does not implements Length property because
            // it is implemented by AmbientArray type during method resolution there.
            return new[]
            {
                Create<ArrayLiteral>(AmbientName, Symbol("add"), Add),
                Create<ArrayLiteral>(AmbientName, Symbol("addRange"), AddRange, rest: true),
                Create<ArrayLiteral>(AmbientName, Symbol("push"), Add),
                CreateN<ArrayLiteral>(AmbientName, Symbol("concat"), Concat),
                Create<ArrayLiteral>(AmbientName, Symbol("map"), Map),
                Create<ArrayLiteral>(AmbientName, Symbol("mapDefined"), MapDefined),
                Create<ArrayLiteral>(AmbientName, Symbol("mapMany"), MapMany),
                Create<ArrayLiteral>(AmbientName, Symbol("mapWithState"), MapWithState),
                Create<ArrayLiteral>(AmbientName, Symbol("mapManyWithState"), MapManyWithState),
                Create<ArrayLiteral>(AmbientName, Symbol("groupBy"), GroupBy),
                Create<ArrayLiteral>(AmbientName, Symbol("reduce"), Reduce),
                Create<ArrayLiteral>(AmbientName, Symbol("select"), Map),
                Create<ArrayLiteral>(AmbientName, Symbol("where"), Filter),
                Create<ArrayLiteral>(AmbientName, Symbol("filter"), Filter),
                Create<ArrayLiteral>(AmbientName, Symbol("indexOf"), IndexOf),
                Create<ArrayLiteral>(AmbientName, Symbol("toSet"), ToSet),
                Create<ArrayLiteral>(AmbientName, Symbol("join"), Join),
                Create<ArrayLiteral>(AmbientName, Symbol("reverse"), Reverse),
                Create<ArrayLiteral>(AmbientName, Symbol("some"), Some),
                Create<ArrayLiteral>(AmbientName, Symbol("all"), All),
                Create<ArrayLiteral>(AmbientName, Symbol("every"), All),
                Create<ArrayLiteral>(AmbientName, Symbol("unique"), Unique),
                Create<ArrayLiteral>(AmbientName, Symbol("slice"), Slice),
                Create<ArrayLiteral>(AmbientName, Symbol("sort"), Sort, minArity: 0),
                Create<ArrayLiteral>(AmbientName, Symbol("isEmpty"), IsEmpty),
                Create<ArrayLiteral>(AmbientName, Symbol("zipWith"), ZipWith),
                Create<ArrayLiteral>(AmbientName, Symbol("withCustomMerge"), WithCustomMerge),
                Create<ArrayLiteral>(AmbientName, Symbol("appendWhenMerged"), AppendWhenMerged),
                Create<ArrayLiteral>(AmbientName, Symbol("prependWhenMerged"), PrependWhenMerged),
                Create<ArrayLiteral>(AmbientName, Symbol("replaceWhenMerged"), ReplaceWhenMerged),
            }.ToDictionary(m => m.Name.StringId, m => m);
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "Array",
                new[]
                {
                    Function(
                        "range",
                        Range,
                        CreateSignature(
                            required: RequiredParameters(PrimitiveType.NumberType, PrimitiveType.NumberType),
                            optional: OptionalParameters(PrimitiveType.NumberType),
                            returnType: AmbientTypes.ArrayType))
                });
        }

        private static EvaluationResult Add(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var result = new EvaluationResult[receiver.Length + 1];
            receiver.Copy(0, result, 0, receiver.Length);
            result[receiver.Length] = arg;

            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult AddRange(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var arrayArg = Converter.ExpectArrayLiteral(arg);
            if (arrayArg.Length == 0)
            {
                return EvaluationResult.Create(receiver);
            }

            if (receiver.Length == 0)
            {
                return EvaluationResult.Create(arrayArg);
            }

            var result = new EvaluationResult[receiver.Length + arrayArg.Length];
            receiver.Copy(0, result, 0, receiver.Length);
            arrayArg.Copy(0, result, receiver.Length, arrayArg.Length);

            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Concat(Context context, ArrayLiteral receiver, EvaluationResult[] args, EvaluationStackFrame captures)
        {
            if (args.Length == 0)
            {
                // Return itself if no argument is passed. This is correct because our array literal is immutable.
                return EvaluationResult.Create(receiver);
            }

            var count = 0;

            var arrays = new ArrayLiteral[args.Length];

            for (int i = 0; i < args.Length; ++i)
            {
                // The code used to be: arrays[i] = Args.AsArrayLiteral(args, i);
                // This shows up as one of the most expensive operation during profiling and micro benchmarking.
                // Actually the index argument check is unnecessary. Moreover, from profiler and micro benchmarking,
                // it turns out that "T x = value as T" (called by AsArrayLiteral), where T is class, is expensive.
                // For more information see Converter.ExpectRef.
                // Array concat is quite pervasive, so specializing this part is worth doing.
                if (!(args[i].Value is ArrayLiteral aTemp))
                {
                    throw Converter.CreateException<ArrayLiteral>(args[i], new ConversionContext(pos: i + 1));
                }

                arrays[i] = aTemp;
                count += arrays[i].Length;
            }

            if (count == 0)
            {
                return EvaluationResult.Create(receiver);
            }

            var result = new EvaluationResult[receiver.Length + count];

            var nextIndex = receiver.Length;

            if (nextIndex > 0)
            {
                receiver.Copy(0, result, 0, nextIndex);
            }

            for (int i = 0; i < arrays.Length; ++i)
            {
                var array = arrays[i];
                var length = array.Length;

                if (length > 0)
                {
                    array.Copy(0, result, nextIndex, length);
                }

                nextIndex += length;
            }

            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Map(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg);

            int paramsCount = closure.Function.Params;

            if (receiver.Length == 0)
            {
                return EvaluationResult.Create(receiver);
            }

            var result = new EvaluationResult[receiver.Length];

            using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
            {
                for (int i = 0; i < receiver.Length; ++i)
                {
                    frame.TrySetArguments(paramsCount, receiver[i], i, EvaluationResult.Create(receiver));
                    result[i] = context.InvokeClosure(closure, frame);

                    if (result[i].IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }
                }

                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, context.TopStack.InvocationLocation, context.TopStack.Path));
            }
        }

        private static EvaluationResult MapDefined(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg);
            int paramsCount = closure.Function.Params;

            if (receiver.Length == 0)
            {
                return EvaluationResult.Create(receiver);
            }

            var result = new EvaluationResult[receiver.Length];
            using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
            {
                int j = 0;
                for (int i = 0; i < receiver.Length; ++i)
                {
                    frame.TrySetArguments(paramsCount, receiver[i], i, EvaluationResult.Create(receiver));
                    EvaluationResult r = context.InvokeClosure(closure, frame);

                    if (r.IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }

                    // Skip the result if undefined.
                    if (!r.IsUndefined)
                    {
                        result[j] = r;
                        ++j;
                    }
                }

                var definedResult = new EvaluationResult[j];
                Array.Copy(result, 0, definedResult, 0, j);
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(definedResult, context.TopStack.InvocationLocation, context.TopStack.Path));
            }
        }

        private static EvaluationResult MapMany(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg);
            int paramsCount = closure.Function.Params;
            if (receiver.Length == 0)
            {
                return EvaluationResult.Create(receiver);
            }

            // WIP: try to pool this
            var arrays = new ArrayLiteral[receiver.Length];
            int count = 0;

            using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
            {
                for (int i = 0; i < receiver.Length; ++i)
                {
                    frame.TrySetArguments(paramsCount, receiver[i], i, EvaluationResult.Create(receiver));
                    EvaluationResult mapResult = context.InvokeClosure(closure, frame);

                    if (mapResult.IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }

                    if (!(mapResult.Value is ArrayLiteral mappedElems))
                    {
                        throw Converter.CreateException<ArrayLiteral>(mapResult, default(ConversionContext));
                    }

                    arrays[i] = mappedElems;
                    count += mappedElems.Length;
                }

                if (count == 0)
                {
                    return EvaluationResult.Create(arrays[0]);
                }

                var result = new EvaluationResult[count];
                var nextIndex = 0;

                for (int i = 0; i < arrays.Length; ++i)
                {
                    var array = arrays[i];
                    var length = array.Length;

                    if (length > 0)
                    {
                        array.Copy(0, result, nextIndex, length);
                    }

                    nextIndex += length;
                }

                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, context.TopStack.InvocationLocation, context.TopStack.Path));
            }

        }

        private static EvaluationResult GroupBy(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg);
            using (var disposableFrame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
            {
                // To avoid warning: access to disposable closure.
                var frame = disposableFrame;
                var entry = context.TopStack;

                var result = receiver.Values.GroupBy(obj =>
                {
                    frame.SetArgument(0, obj);
                    return context.InvokeClosure(closure, frame);
                });

                var arr = result.Select(grp =>
                {
                    var grpArrayLit = ArrayLiteral.CreateWithoutCopy(grp.ToArray(), entry.InvocationLocation, entry.Path);
                    var bindings = new List<Binding>
                                    {
                                        new Binding(StringId.Create(context.FrontEndContext.StringTable, "key"), grp.Key, location: default(LineInfo)),
                                        new Binding(StringId.Create(context.FrontEndContext.StringTable, "values"), grpArrayLit, location: default(LineInfo)),
                                    };
                    return EvaluationResult.Create(ObjectLiteral.Create(bindings, entry.InvocationLocation, entry.Path));
                }).ToArray();

                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(arr, entry.InvocationLocation, entry.Path));
            }
        }

        private static EvaluationResult Reduce(Context context, ArrayLiteral receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg0);
            int paramsCount = closure.Function.Params;

            var accumulator = arg1;
            if (receiver.Length > 0)
            {
                using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
                {
                    for (int i = 0; i < receiver.Length; i++)
                    {
                        frame.TrySetArguments(paramsCount, accumulator, receiver[i], i, EvaluationResult.Create(receiver));
                        accumulator = context.InvokeClosure(closure, frame);
                        if (accumulator.IsErrorValue)
                        {
                            return EvaluationResult.Error;
                        }
                    }
                }
            }

            return accumulator;
        }

        private static EvaluationResult MapWithState(Context context, ArrayLiteral receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg0);
            int paramsCount = closure.Function.Params;
            var state = arg1;
            var arrays = new EvaluationResult[receiver.Length];

            using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
            {
                var entry = context.TopStack;

                var stateName = SymbolAtom.Create(context.FrontEndContext.StringTable, "state");
                var elemsName = SymbolAtom.Create(context.FrontEndContext.StringTable, "elems");
                var elemName = SymbolAtom.Create(context.FrontEndContext.StringTable, "elem");

                for (int i = 0; i < receiver.Length; ++i)
                {
                    frame.TrySetArguments(paramsCount, state, receiver[i], i, EvaluationResult.Create(receiver));
                    EvaluationResult mapResult = context.InvokeClosure(closure, frame);

                    if (mapResult.IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }

                    if (!(mapResult.Value is ObjectLiteral objectResult))
                    {
                        throw Converter.CreateException<ObjectLiteral>(mapResult, default(ConversionContext));
                    }

                    arrays[i] = objectResult[elemName];
                    state = objectResult[stateName];
                }

                var bindings = new List<Binding>
                {
                    new Binding(elemsName, ArrayLiteral.CreateWithoutCopy(arrays, entry.InvocationLocation, entry.Path), location: default(LineInfo)),
                    new Binding(stateName, state, location: default(LineInfo)),
                };
                return EvaluationResult.Create(ObjectLiteral.Create(bindings, entry.InvocationLocation, entry.Path));
            }
        }

        private static EvaluationResult MapManyWithState(Context context, ArrayLiteral receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg0);
            int paramsCount = closure.Function.Params;

            var state = arg1;
            var arrays = new ArrayLiteral[receiver.Length];
            int count = 0;
            using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
            {
                var entry = context.TopStack;

                var stateName = SymbolAtom.Create(context.FrontEndContext.StringTable, "state");
                var elemsName = SymbolAtom.Create(context.FrontEndContext.StringTable, "elems");

                for (int i = 0; i < receiver.Length; ++i)
                {
                    frame.TrySetArguments(paramsCount, state, receiver[i], i, EvaluationResult.Create(receiver));
                    EvaluationResult mapResult = context.InvokeClosure(closure, frame);

                    if (mapResult.IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }

                    if (!(mapResult.Value is ObjectLiteral objectResult))
                    {
                        throw Converter.CreateException<ObjectLiteral>(mapResult, default(ConversionContext));
                    }

                    if (!(objectResult[elemsName].Value is ArrayLiteral mappedElems))
                    {
                        throw Converter.CreateException<ArrayLiteral>(mapResult, new ConversionContext(objectCtx: objectResult, name: stateName));
                    }

                    arrays[i] = mappedElems;
                    count += mappedElems.Length;
                    state = objectResult[stateName];
                }

                var result = new EvaluationResult[count];
                var nextIndex = 0;

                for (int i = 0; i < arrays.Length; ++i)
                {
                    var array = arrays[i];
                    var length = array.Length;

                    if (length > 0)
                    {
                        array.Copy(0, result, nextIndex, length);
                    }

                    nextIndex += length;
                }

                var bindings = new List<Binding>
                {
                    new Binding(elemsName, ArrayLiteral.CreateWithoutCopy(result, entry.InvocationLocation, entry.Path), location: default(LineInfo)),
                    new Binding(stateName, state, location: default(LineInfo)),
                };
                return EvaluationResult.Create(ObjectLiteral.Create(bindings, entry.InvocationLocation, entry.Path));
            }
        }

        private static bool IsSpecialUndefinedCheck(Closure closure)
        {
            // TODO: this optimization should happen during Ast Convertion, not here!
            var body = closure.Function.Body;
            if (body == null)
            {
                return false;
            }

            var returnStatement = body as ReturnStatement;

            var binaryExpression = returnStatement?.ReturnExpression as BinaryExpression;

            // closure has a following content: x => return left OP right;
            if (
                (binaryExpression?.LeftExpression as LocalReferenceExpression)?.Index == 0 &&
                binaryExpression.OperatorKind == BinaryOperator.NotEqual &&
                binaryExpression.RightExpression == UndefinedLiteral.Instance)
            {
                // got the match!
                return true;
            }

            return false;
        }

        private static EvaluationResult Filter(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg);
            if (receiver.Length == 0)
            {
                return EvaluationResult.Create(receiver);
            }

            // [a,b,c].filter(x => x !== undefined) is a common pattern.
            // Using a specialized filtering logic improves performance.
            bool isSpecialUndefinedCheck = IsSpecialUndefinedCheck(closure);

            // The following variable contains a list of indices that changed the predicate value from true to false and vice versa.
            // Consider following example:
            // [1,2,3,4,5].filter(x => x > 3);
            // In this case we'll get the following values in indices variable:
            // [0, // starting index: assume that predicate is true at the beginning
            // 0, // the first element yields 'false'
            // 3, // 3-d index yield 'true'
            // 5] // will contains the size of the array if the predicate yielded 'true' at the end of the loop.
            // To filter the array using this table, we just need to copy all
            // elements in every range: 'even index' to 'even index + 1' because those ranges contains
            // indices when predicate yielded 'true'.

            // This approach saves both memory and improve perf (0.5Gb for Analog repo and even more for OneCore).
            // Instead of allocating another array this function will allocate the list that will be
            // twice as small in the worst case (when every element yields different result).
            List<int> indices = new List<int>() { 0 };

            bool lastPredicate = true;
            int resultingLength = 0;
            int paramsCount = closure.Function.Params;

            // Allocating the frame.

            // Needed only for non-optimized version. If the predicate is just a null check, no frames were needed.
            using (var frame = isSpecialUndefinedCheck ? default(EvaluationStackFrame) : EvaluationStackFrame.Create(closure.Function, captures.Frame))
            {
                int i;
                for (i = 0; i < receiver.Length; ++i)
                {
                    EvaluationResult valueToCompare = receiver[i];

                    // In a better world, the following logic would be extracted in the separate method.
                    // But in the current one it will require a method with 7-8 arguments.
                    // So, keeping the logic right in the loop.
                    bool predicateResult;
                    if (isSpecialUndefinedCheck)
                    {
                        predicateResult = !valueToCompare.IsUndefined;
                    }
                    else
                    {
                        frame.TrySetArguments(paramsCount, receiver[i], i, EvaluationResult.Create(receiver));
                        valueToCompare = context.InvokeClosure(closure, frame);

                        if (valueToCompare.IsErrorValue)
                        {
                            return EvaluationResult.Error;
                        }

                        predicateResult = Expression.IsTruthy(valueToCompare);
                    }

                    if (predicateResult != lastPredicate)
                    {
                        lastPredicate = predicateResult;
                        indices.Add(i);
                    }

                    if (predicateResult)
                    {
                        resultingLength++;
                    }
                }

                if (lastPredicate)
                {
                    indices.Add(i);
                }

                if (resultingLength == receiver.Length)
                {
                    // Nothing was filtered.
                    // Just returning the same result.
                    return EvaluationResult.Create(receiver);
                }

                EvaluationResult[] result = resultingLength == 0 ? CollectionUtilities.EmptyArray<EvaluationResult>() : new EvaluationResult[resultingLength];

                // Now we need to copy all the items from the source array
                int targetIndex = 0;
                for (int idx = 0; idx < indices.Count; idx += 2)
                {
                    int sourceIndex = indices[idx];
                    int length = indices[idx + 1] - sourceIndex;
                    receiver.Copy(sourceIndex, result, targetIndex, length);
                    targetIndex += length;
                }

                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, context.TopStack.InvocationLocation, context.TopStack.Path));
            }
        }

        private static EvaluationResult IndexOf(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var count = receiver.Length;
            for (int i = 0; i < count; ++i)
            {
                if (receiver[i].Equals(arg))
                {
                    return EvaluationResult.Create(i);
                }
            }

            return EvaluationResult.Create(-1);
        }

        private static EvaluationResult ToSet(Context context, ArrayLiteral receiver, EvaluationStackFrame captures)
        {
            return Unique(context, receiver, captures);
        }

        private static EvaluationResult Unique(Context context, ArrayLiteral receiver, EvaluationStackFrame captures)
        {
            if (receiver.Length == 0)
            {
                return EvaluationResult.Create(receiver);
            }

            // note that HashSet preserves insertion order
            var unique = new HashSet<EvaluationResult>(receiver.Values).ToArray();
            if (unique.Length == receiver.Length)
            {
                return EvaluationResult.Create(receiver);
            }

            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(unique, entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Join(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var arrays = Converter.ExpectStringArray(receiver);
            var sep = Converter.ExpectString(arg);

            return EvaluationResult.Create(string.Join(sep, arrays));
        }

        private static EvaluationResult Reverse(Context context, ArrayLiteral receiver, EvaluationStackFrame captures)
        {
            var reversed = receiver.Values.Reverse().ToArray();
            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(reversed, entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Some(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return SomeOrAll(context, receiver, arg, captures, true);
        }

        private static EvaluationResult All(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return SomeOrAll(context, receiver, arg, captures, false);
        }

        private static EvaluationResult IsEmpty(Context context, ArrayLiteral receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Length == 0);
        }

        private static EvaluationResult SomeOrAll(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures, bool isSome)
        {
            var closure = Converter.ExpectClosure(arg);
            if (receiver.Length == 0)
            {
                return EvaluationResult.Create(!isSome);
            }

            int paramsCount = closure.Function.Params;

            using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
            {
                for (int i = 0; i < receiver.Length; ++i)
                {
                    frame.TrySetArguments(paramsCount, receiver[i], i, EvaluationResult.Create(receiver));
                    EvaluationResult lambdaResult = context.InvokeClosure(closure, frame);

                    if (lambdaResult.IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }

                    bool isTrue = Expression.IsTruthy(lambdaResult);
                    if (isSome && isTrue)
                    {
                        return EvaluationResult.True;
                    }

                    if (!isSome && !isTrue)
                    {
                        return EvaluationResult.False;
                    }
                }

                // if isSome ("some") and we've exhausted the loop (no elem matched) ==> return false
                // if !isSome ("all") and we've exhausted the loop (all elems matched) ==> return true
                return EvaluationResult.Create(!isSome);
            }
        }

        private static EvaluationResult Slice(Context context, ArrayLiteral receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            var entry = context.TopStack;

            int length = receiver.Length;

            int start = Converter.ExpectNumber(arg0, context: new ConversionContext(pos: 1));
            start = start < 0 ? Math.Max(length + start, 0) : Math.Min(start, length); // Support indexing from the end

            int end;
            if (arg1.IsUndefined)
            {
                end = length;
            }
            else
            {
                end = Converter.ExpectNumber(arg1, context: new ConversionContext(pos: 2));
                end = end < 0 ? Math.Max(length + end, start) : Math.Min(end, length); // Support indexing from the end
            }

            int sliceLength = end - start;
            var slice = new EvaluationResult[sliceLength];
            receiver.Copy(start, slice, 0, sliceLength);

            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(slice, entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult Sort(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            if (receiver.Length <= 1)
            {
                return EvaluationResult.Create(receiver);
            }

            bool hasErrors = false;
            var closure = Converter.ExpectClosure(arg, new ConversionContext(allowUndefined: true, pos: 1));

            IEnumerable<EvaluationResult> sortedArray;
            if (closure == null)
            {
                EvaluationResult firstElem = receiver.Values.First();
                object firstValue = firstElem.Value;
                if (firstValue is int)
                {
                    sortedArray = receiver.Values.OrderBy(e => Converter.ExpectNumber(e, context: new ConversionContext(objectCtx: e)), Comparer<int>.Default);
                }
                else if (firstValue is string)
                {
                    sortedArray = receiver.Values.OrderBy(e => Converter.ExpectString(e, context: new ConversionContext(objectCtx: e)), Comparer<string>.Default);
                }
                else
                {
                    throw Converter.CreateException(
                        expectedTypes: new[] { typeof(int), typeof(string) },
                        value: firstElem,
                        context: new ConversionContext(pos: 1, objectCtx: receiver));
                }
            }
            else
            {
                using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
                {
                    sortedArray = receiver.Values.OrderBy(e => e, Comparer<EvaluationResult>.Create((lhs, rhs) =>
                    {
                        if (lhs.IsErrorValue || rhs.IsErrorValue)
                        {
                            hasErrors = true;
                            return 0;
                        }

                        frame.SetArgument(0, lhs);
                        frame.SetArgument(1, rhs);
                        EvaluationResult compareResult = context.InvokeClosure(closure, frame);
                        if (compareResult.IsErrorValue)
                        {
                            hasErrors = true;
                            return 0;
                        }

                        return Converter.ExpectNumber(compareResult, context: new ConversionContext(objectCtx: closure));
                    }));
                }
            }

            var entry = context.TopStack;
            var sortedArrayMaterialized = sortedArray.ToArray();
            var result = hasErrors 
                ? (object)ErrorValue.Instance
                : ArrayLiteral.CreateWithoutCopy(sortedArrayMaterialized, entry.InvocationLocation, entry.Path);
            return EvaluationResult.Create(result);
        }

        private static EvaluationResult ZipWith(Context context, ArrayLiteral receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            var arrayToZipWith = Converter.ExpectArrayLiteral(arg0);
            var closure = Converter.ExpectClosure(arg1);
            int paramsCount = closure.Function.Params;

            if (receiver.Length == 0)
            {
                return EvaluationResult.Create(receiver);
            }

            if (arrayToZipWith.Length == 0)
            {
                return EvaluationResult.Create(arrayToZipWith);
            }

            // The returned array has the size of the smallest array
            var result = new EvaluationResult[Math.Min(receiver.Length, arrayToZipWith.Length)];

            using (var frame = EvaluationStackFrame.Create(closure.Function, captures.Frame))
            {
                for (int i = 0; i < result.Length; ++i)
                {
                    // zipWith callback takes 2 arguments: from the receiver and from the first argument of a zipWith function.
                    frame.TrySetArguments(paramsCount, receiver[i], arrayToZipWith[i]);
                    result[i] = context.InvokeClosure(closure, frame);

                    if (result[i].IsErrorValue)
                    {
                        return EvaluationResult.Error;
                    }
                }

                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, context.TopStack.InvocationLocation, context.TopStack.Path));
            }
        }

        private static EvaluationResult WithCustomMerge(Context context, ArrayLiteral receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var closure = Converter.ExpectClosure(arg);

            var entry = context.TopStack;
            var invocationLocation = entry.InvocationLocation;
            var invocationPath = entry.Path;

            return EvaluationResult.Create(
                ArrayLiteralWithCustomMerge.Create(receiver, closure, invocationLocation, invocationPath));
        }

        private static EvaluationResult AppendWhenMerged(
            Context context,
            ArrayLiteral receiver,
            EvaluationStackFrame captures)
        {
            // In this case we use the append function defined in ArrayLiteral, since 
            // it is the default one
            return WithCustomNativeMerge(context, receiver, ArrayLiteral.MergeAppend());
        }

        private static EvaluationResult PrependWhenMerged(
            Context context,
            ArrayLiteral receiver,
            EvaluationStackFrame captures)
        {
            return WithCustomNativeMerge(context, receiver, Prepend);

            EvaluationResult Prepend(EvaluationResult leftObject, EvaluationResult rightObject)
            {
                // if the right object is not an array, then this acts as an override
                if (!(rightObject.Value is ArrayLiteral))
                {
                    return rightObject;
                }

                // Once we know the right object is an array, prepending is equivalent to appending in reverse order
                return ArrayLiteral.MergeAppend()(rightObject, leftObject);
            }
        }

        /** 
         * Returns an integer array of increasing elements from 'start' to 'stop' (including both 'start' and 'stop').
         * If 'start > stop' the function returns the empty array.
         */
        private static EvaluationResult Range(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            Args.CheckArgumentIndex(args, 0);
            var start = Converter.ExpectNumber(args[0], position: 0);

            Args.CheckArgumentIndex(args, 1);
            var stop = Converter.ExpectNumber(args[1], position: 1);

            var entry = context.TopStack;
            if (start > stop)
            {
                return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(CollectionUtilities.EmptyArray<EvaluationResult>(), entry.InvocationLocation, entry.Path));
            }

            var step = (args.Length > 2 && !args[2].IsUndefined)
                ? Converter.ExpectNumber(args[2], position: 2)
                : 1;

            if (step <= 0)
            {
                throw Converter.CreateException("Expected positive step in Array.range", EvaluationResult.Create(step), new ConversionContext(objectCtx: args, pos: 2));
            }

            var length = (stop - start + step - 1) / step + 1;
            var result = new List<EvaluationResult>(length);

            for (int i = start; i < stop; i += step)
            {
                result.Add(EvaluationResult.Create(i));
            }

            result.Add(EvaluationResult.Create(stop));

            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result.ToArray(), entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult ReplaceWhenMerged(
            Context context,
            ArrayLiteral receiver,
            EvaluationStackFrame captures)
        {
            EvaluationResult Replace(EvaluationResult right, EvaluationResult left) => left;

            return WithCustomNativeMerge(context, receiver, Replace);
        }

        private static EvaluationResult WithCustomNativeMerge(Context context, ArrayLiteral receiver, ObjectLiteral.MergeFunction customMerge)
        {
            var entry = context.TopStack;
            var invocationLocation = entry.InvocationLocation;
            var invocationPath = entry.Path;

            return EvaluationResult.Create(
                ArrayLiteralWithNativeCustomMerge.Create(receiver, customMerge, invocationLocation, invocationPath));
        }
    }
}

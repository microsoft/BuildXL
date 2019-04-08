// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition for <code>namespace String {}</code> and <code>interface String {}</code>.
    /// </summary>
    public sealed class AmbientString : AmbientDefinition<string>
    {
        /// <nodoc />
        public AmbientString(PrimitiveTypes knownTypes)
            : base("String", knownTypes)
        {
        }

        private CallSignature IsUndefinedOrEmptySignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.BooleanType);

        private CallSignature IsUndefinedOrWhitespaceSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType),
            returnType: AmbientTypes.BooleanType);

        private CallSignature JoinSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.StringType, new ArrayType(AmbientTypes.StringType)),
            returnType: AmbientTypes.StringType);

        private CallSignature FromCharCodeSignature => CreateSignature(
            required: RequiredParameters(new ArrayType(AmbientTypes.NumberType)),
            returnType: AmbientTypes.StringType);

        /// <summary>
        ///     Signature for string interpolation
        /// </summary>
        private CallSignature InterpolateSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.ObjectType),
            restParameterType: AmbientTypes.ObjectType,
            returnType: AmbientTypes.StringType);

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                "String",
                new[]
                {
                    // For testing purposes only: to validate that '_' is a valid identifier character..
                    // We use triple underscore because double underscore gets mangled into triple underscore
                    // by the parser
#if DEBUG
                    Function("___isUndefinedOrEmpty", IsUndefinedOrEmpty, IsUndefinedOrEmptySignature),
#endif
                    Function("interpolate", Interpolate, InterpolateSignature),
                    Function("isUndefinedOrEmpty", IsUndefinedOrEmpty, IsUndefinedOrEmptySignature),
                    Function("isUndefinedOrWhitespace", IsUndefinedOrWhitespace, IsUndefinedOrWhitespaceSignature),
                    Function("join", Join, JoinSignature),
                    Function("fromCharCode", FromCharCode, FromCharCodeSignature),
                });
        }

        /// <inheritdoc />
        protected override Dictionary<StringId, CallableMember<string>> CreateMembers()
        {
            return new[]
            {
#if DEBUG
                // We use triple underscore because double underscore gets mangled into triple underscore
                // by the parser
                Create<string>(AmbientName, Symbol("___charAt"), CharAt),
                CreateProperty<string>(AmbientName, Symbol("___length"), Length),
#endif
                Create<string>(AmbientName, Symbol("charAt"), CharAt),
                Create<string>(AmbientName, Symbol("charCodeAt"), CharCodeAt),
                Create<string>(AmbientName, Symbol("concat"), Concat, rest: true),

                // concat takes an array as a second argument, not just string.
                Create<string>(AmbientName, Symbol("contains"), Contains),
                Create<string>(AmbientName, Symbol("endsWith"), EndsWith),
                Create<string>(AmbientName, Symbol("indexOf"), IndexOf, requiredNumberOfArguments: 1),
                Create<string>(AmbientName, Symbol("lastIndexOf"), LastIndexOf, requiredNumberOfArguments: 1),
                CreateProperty<string>(AmbientName, Symbol("length"), Length),
                Create<string>(AmbientName, Symbol("localeCompare"), LocaleCompare),
                Create<string>(AmbientName, Symbol("replace"), Replace),
                Create<string>(AmbientName, Symbol("slice"), Slice, requiredNumberOfArguments: 0),
                Create<string>(AmbientName, Symbol("split"), Split, requiredNumberOfArguments: 1),
                Create<string>(AmbientName, Symbol("startsWith"), StartsWith),
                Create<string>(AmbientName, Symbol("toLowerCase"), ToLowerCase),
                Create<string>(AmbientName, Symbol("toUpperCase"), ToUpperCase),
                Create<string>(AmbientName, Symbol("toUpperCaseFirst"), ToUpperCaseFirst),
                Create<string>(AmbientName, Symbol("toLowerCaseFirst"), ToLowerCaseFirst),
                Create<string>(AmbientName, Symbol("trim"), Trim, minArity: 0),
                Create<string>(AmbientName, Symbol("trimEnd"), TrimEnd, minArity: 0),
                Create<string>(AmbientName, Symbol("trimStart"), TrimStart, minArity: 0),
                Create<string>(AmbientName, Symbol("toArray"), ToArray),
            }.ToDictionary(m => m.Name.StringId, m => m);
        }

        private static EvaluationResult IsUndefinedOrEmpty(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var str = Args.AsStringOrUndefined(args, 0);
            return EvaluationResult.Create(string.IsNullOrEmpty(str));
        }

        private static EvaluationResult IsUndefinedOrWhitespace(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var str = Args.AsStringOrUndefined(args, 0);
            return EvaluationResult.Create(string.IsNullOrWhiteSpace(str));
        }

        private static EvaluationResult Join(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var separator = Args.AsString(args, 0);
            var arrayOfStrings = Args.AsArrayLiteral(args, 1);

            using (var pooledInstance = Pools.StringBuilderPool.GetInstance())
            {
                var builder = pooledInstance.Instance;
                for (var i = 0; i < arrayOfStrings.Length; ++i)
                {
                    var s = Converter.ExpectString(
                        arrayOfStrings[i],
                        new ConversionContext(pos: i, objectCtx: arrayOfStrings));
                    builder.Append(s);

                    if (i < arrayOfStrings.Length - 1)
                    {
                        builder.Append(separator);
                    }
                }

                return EvaluationResult.Create(builder.ToString());
            }
        }

        private static EvaluationResult FromCharCode(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var codes = Args.AsArrayLiteral(args, 0);
            var chars = codes.Values.Select((elem, idx) =>
            {
                int code = Converter.ExpectNumber(elem, strict: true, context: new ConversionContext(objectCtx: codes, pos: idx));
                unchecked
                {
                    return (char)code;
                }
            }).ToArray();
            return EvaluationResult.Create(new string(chars));
        }

        private static EvaluationResult CharAt(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(CharAtCore(receiver, arg));
        }

        private static string CharAtCore(string receiver, EvaluationResult arg)
        {
            var index = Converter.ExpectNumber(arg);

            if (index < 0 || index >= receiver.Length)
            {
                // Typescript/Javascript would return empty string, but we want to be stricter.
                throw new StringIndexOutOfBoundException(index, receiver);
            }

            return receiver[index].ToString();
        }

        private static EvaluationResult CharCodeAt(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(CharAtCore(receiver, arg)[0]);
        }

        private static EvaluationResult Concat(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var arrayArg = Converter.ExpectArrayLiteral(arg);

            switch (arrayArg.Length)
            {
                case 1:
                    return EvaluationResult.Create(
                        string.Concat(
                            receiver,
                            Converter.ExpectString(arrayArg[0], new ConversionContext(pos: 0, objectCtx: arrayArg))));
                case 2:
                    return EvaluationResult.Create(
                        string.Concat(
                            receiver,
                            Converter.ExpectString(arrayArg[0], new ConversionContext(pos: 0, objectCtx: arrayArg)),
                            Converter.ExpectString(arrayArg[1], new ConversionContext(pos: 1, objectCtx: arrayArg))));
                default:
                    var values = new string[arrayArg.Length + 1];
                    values[0] = receiver;
                    for (var i = 1; i < values.Length; ++i)
                    {
                        values[i] = Converter.ExpectString(
                            arrayArg[i - 1],
                            new ConversionContext(pos: i - 1, objectCtx: arrayArg));
                    }

                    return EvaluationResult.Create(string.Concat(values));
            }
        }

        private static EvaluationResult Contains(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Contains(Converter.ExpectString(arg)));
        }

        private static EvaluationResult EndsWith(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.EndsWith(Converter.ExpectString(arg), StringComparison.Ordinal));
        }

        private static EvaluationResult IndexOf(Context context, string receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            var arg0Str = Converter.ExpectString(arg0, new ConversionContext(pos: 1));

            if (arg1.IsUndefined)
            {
                return EvaluationResult.Create(receiver.IndexOf(arg0Str, StringComparison.Ordinal));
            }

            return EvaluationResult.Create(
                receiver.IndexOf(arg0Str, Converter.ExpectNumber(arg1, context: new ConversionContext(pos: 2)),
                    StringComparison.Ordinal));
        }

        private static EvaluationResult LastIndexOf(Context context, string receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            var arg0Str = Converter.ExpectString(arg0, new ConversionContext(pos: 1));
            var arg1Int = arg1.IsUndefined
                ? receiver.Length
                : Converter.ExpectNumber(arg1, context: new ConversionContext(pos: 2));

            if (arg1Int < 0)
            {
                arg1Int = 0;
            }

            if (arg1Int > receiver.Length)
            {
                arg1Int = receiver.Length;
            }

            return EvaluationResult.Create(receiver.LastIndexOf(arg0Str, arg1Int, StringComparison.Ordinal));
        }

        private static EvaluationResult Length(Context context, string receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.Length);
        }

        private static EvaluationResult LocaleCompare(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            var rhsString = Converter.ExpectString(arg, new ConversionContext(allowUndefined: true, objectCtx: arg.Value));
            return EvaluationResult.Create(rhsString == null
                ? -1
                : string.Compare(receiver, rhsString, StringComparison.CurrentCulture));
        }

        private static EvaluationResult Replace(Context context, string receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(
                receiver.Replace(
                    Converter.ExpectString(arg0, new ConversionContext(pos: 1)),
                    Converter.ExpectString(arg1, new ConversionContext(pos: 2))));
        }

        private static EvaluationResult Slice(Context context, string receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            var start = arg0.IsUndefined
                ? 0
                : Converter.ExpectNumber(arg0, context: new ConversionContext(pos: 1));

            var end = arg1.IsUndefined
                ? receiver.Length
                : Converter.ExpectNumber(arg1, context: new ConversionContext(pos: 2));

            // this seems to be the semantics in JavaScript (if 'end' is greater than length,
            // it's trimmed down to length; but if 'start' is less than 0, the result is the empty string)
            if (end > receiver.Length)
            {
                end = receiver.Length;
            }

            return (start < 0 || start >= receiver.Length || end <= 0 || end > receiver.Length || end <= start)
                ? EvaluationResult.Create(string.Empty)
                : EvaluationResult.Create(receiver.Substring(start, end - start));
        }

        private static EvaluationResult Split(Context context, string receiver, EvaluationResult arg0, EvaluationResult arg1, EvaluationStackFrame captures)
        {
            var separator = Converter.ExpectString(arg0, new ConversionContext(pos: 1));
            var resultAsStrs = receiver.Split(new[] { separator }, int.MaxValue, StringSplitOptions.None);

            // Here, C# and ECMAScript semantics differ:
            // ECMAScript: ";aa;;bb;".split(";", 3) will yield ["", "aa", ""]
            // C#: ";aa;;bb;".split(";", 3) will yield ["", "aa", ";bb;"]
            // (i.e., C# stops splitting, and ECMAScript truncates)
            var len = arg1.IsUndefined ? resultAsStrs.Length : Converter.ExpectNumber(arg1, context: new ConversionContext(pos: 2));

            var result = new EvaluationResult[len];
            for (int i = 0; i < len; i++)
            {
                result[i] = EvaluationResult.Create(resultAsStrs[i]);
            }

            var entry = context.TopStack;
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(result, entry.InvocationLocation, entry.Path));
        }

        private static EvaluationResult StartsWith(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.StartsWith(Converter.ExpectString(arg), StringComparison.Ordinal));
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase")]
        private static EvaluationResult ToLowerCase(Context context, string receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.ToLowerInvariant());
        }

        private static EvaluationResult ToLowerCaseFirst(Context context, string receiver, EvaluationStackFrame captures)
        {
            if (string.IsNullOrEmpty(receiver))
            {
                return EvaluationResult.Create(receiver);
            }

            return EvaluationResult.Create(char.ToLowerInvariant(receiver[0]) + (receiver.Length > 1 ? receiver.Substring(1) : string.Empty));
        }

        private static EvaluationResult ToUpperCase(Context context, string receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(receiver.ToUpperInvariant());
        }

        private static EvaluationResult ToUpperCaseFirst(Context context, string receiver, EvaluationStackFrame captures)
        {
            if (string.IsNullOrEmpty(receiver))
            {
                return EvaluationResult.Create(receiver);
            }

            return EvaluationResult.Create(char.ToUpperInvariant(receiver[0]) + (receiver.Length > 1 ? receiver.Substring(1) : string.Empty));
        }

        private static EvaluationResult Trim(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return arg.IsUndefined
                ? EvaluationResult.Create(receiver.Trim())
                : EvaluationResult.Create(receiver.Trim(Converter.ExpectString(arg, new ConversionContext(objectCtx: arg.Value, pos: 1)).ToCharArray()));
        }

        private static EvaluationResult TrimStart(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return arg.IsUndefined
                ? EvaluationResult.Create(receiver.TrimStart())
                : EvaluationResult.Create(receiver.TrimStart(Converter.ExpectString(arg, new ConversionContext(objectCtx: arg.Value, pos: 1)).ToCharArray()));
        }

        private static EvaluationResult TrimEnd(Context context, string receiver, EvaluationResult arg, EvaluationStackFrame captures)
        {
            return arg.IsUndefined
                ? EvaluationResult.Create(receiver.TrimEnd())
                : EvaluationResult.Create(receiver.TrimEnd(Converter.ExpectString(arg, new ConversionContext(objectCtx: arg.Value, pos: 1)).ToCharArray()));
        }

        private static EvaluationResult ToArray(Context context, string receiver, EvaluationStackFrame captures)
        {
            var elems = string.IsNullOrEmpty(receiver)
                ? CollectionUtilities.EmptyArray<EvaluationResult>()
                : receiver.ToCharArray().SelectArray(ch => EvaluationResult.Create(ch.ToString()));
            return EvaluationResult.Create(ArrayLiteral.CreateWithoutCopy(elems, context.TopStack.InvocationLocation, context.TopStack.Path));
        }

        /// <summary>
        ///     Implements string interpolation.
        /// </summary>
        private static EvaluationResult Interpolate(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            var firstArgument = ToStringConverter.ObjectToString(context, args[0]);

            var rest = Args.AsArrayLiteral(args, 1);

            return EvaluationResult.Create(string.Concat(
                firstArgument,
                string.Join(string.Empty, rest.Values.Select(v => ToStringConverter.ObjectToString(context, v)))));
        }
    }
}

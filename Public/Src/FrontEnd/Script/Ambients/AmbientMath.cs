// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Ambient definition that provides basic mathematics functionality.
    /// </summary>
    public sealed class AmbientMath : AmbientDefinitionBase
    {
        internal const string Name = "Math";

        /// <nodoc />
        public AmbientMath(PrimitiveTypes knownTypes)
            : base(Name, knownTypes)
        {
        }

        /// <inheritdoc />
        protected override AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return new AmbientNamespaceDefinition(
                Name,
                new[]
                {
                    Function("abs", Abs, AbsSignature),
                    Function("sum", Sum, SumSignature),
                    Function("max", Max, MinMaxSignature),
                    Function("min", Min, MinMaxSignature),

                    Function("pow", Pow, TwoArgsSignature),
                    Function("mod", Mod, TwoArgsSignature),
                    Function("div", Div, TwoArgsSignature),
                });
        }

        private CallSignature AbsSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.NumberType),
            returnType: AmbientTypes.NumberType);

        private CallSignature SumSignature => CreateSignature(
            restParameterType: AmbientTypes.NumberType,
            returnType: AmbientTypes.NumberType);

        private CallSignature MinMaxSignature => CreateSignature(
            restParameterType: AmbientTypes.NumberType,
            returnType: AmbientTypes.NumberType);

        private CallSignature TwoArgsSignature => CreateSignature(
            required: RequiredParameters(AmbientTypes.NumberType, AmbientTypes.NumberType),
            returnType: AmbientTypes.NumberType);

        private static EvaluationResult Abs(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            int value = Args.AsInt(args, 0);
            
            // Cast to long to force an overflow for int.MinValue
            return DoMath(() => checked((int)Math.Abs((long)value)));
        }

        private static EvaluationResult Sum(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            // The checker should make sure that the function takes numbers.
            var values = Args.AsArrayLiteral(args, 0).Values.Select(v => (int)v.Value).ToList();

            return DoMath(() => checked(values.Sum()));
        }

        private static EvaluationResult Max(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            // The checker should make sure that the function takes numbers.
            var values = Args.AsArrayLiteral(args, 0).Values.Select(v => (int)v.Value).ToList();

            if (values.Count == 0)
            {
                return EvaluationResult.Undefined;
            }

            return DoMath(() => values.Max());
        }

        private static EvaluationResult Min(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            // The checker should make sure that the function takes numbers.
            var values = Args.AsArrayLiteral(args, 0).Values.Select(v => (int)v.Value).ToList();

            if (values.Count == 0)
            {
                return EvaluationResult.Undefined;
            }

            return DoMath(() => values.Min());
        }

        private static EvaluationResult Pow(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            int @base = Args.AsInt(args, 0);
            int exponent = Args.AsInt(args, 1);

            return DoMath(() => checked((int)Math.Pow(@base, exponent)));
        }

        private static EvaluationResult Mod(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            int divident = Args.AsInt(args, 0);
            int divisor = Args.AsInt(args, 1);

            return DoMath(() => divident % divisor);
        }

        private static EvaluationResult Div(Context context, ModuleLiteral env, EvaluationStackFrame args)
        {
            int divident = Args.AsInt(args, 0);
            int divisor = Args.AsInt(args, 1);

            return DoMath(() => divident / divisor);
        }

        private static EvaluationResult DoMath(Func<int> func)
        {
            try
            {
                return EvaluationResult.Create(func());
            }
            catch (OverflowException)
            {
                throw new ArithmeticOverflowException();
            }
            catch (DivideByZeroException)
            {
                throw new MathDivideByZeroException();
            }
        }
    }
}

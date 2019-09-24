// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Statements;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Delegate for calling the ambient function with the C# implementation.
    /// </summary>
    public delegate EvaluationResult InvokeAmbient(Context context, ModuleLiteral env, EvaluationStackFrame frame);

    /// <summary>
    /// Interface with a method for invoking lambda expression or other invocable members.
    /// </summary>
    public interface IInvocable
    {
        /// <nodoc />
        EvaluationResult Invoke(Context context, ModuleLiteral env, EvaluationStackFrame frame);
    }

    /// <summary>
    /// Represents function-like expression.
    /// </summary>
    /// <remarks>
    /// There are 3 function-like expressions in DScript:
    /// * Ambient functions/properties with the body implemented in C#, like <code>export declare function glob()</code>.
    /// * Function-expression, like <code>() => {return 42;}</code>
    /// * Normal functions with a body, like <code>function foo() {return 42;}</code>
    /// </remarks>
    public class FunctionLikeExpression : Expression, IInvocable
    {
        /// <nodoc />
        [NotNull]
        public CallSignature CallSignature { get; }

        /// <summary>
        /// Function body. Null for ambients.
        /// </summary>
        [CanBeNull]
        public Statement Body { get; }

        /// <summary>
        /// Returns true if the current instance represents an abient function call.
        /// </summary>
        public bool IsAmbient => Body == null;

        /// <summary>
        /// Number of captured variables.
        /// </summary>
        public int Captures { get; }

        /// <summary>
        /// Number of locals.
        /// </summary>
        public int Locals { get; }

        /// <summary>
        /// Name of the function. Invalid for lambda expressions.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <summary>
        /// Returns the number of formal parameters
        /// </summary>
        public int Params { get; }

        /// <summary>
        /// Contains invocation statistics for the current function.
        /// </summary>
        [NotNull]
        public FunctionStatistic Statistic { get; }

        // The delegate with the implemnetation of ambient function/property.
        private readonly InvokeAmbient m_ambient;

        /// <summary>
        /// Creates a lambda expression from a call signature and a statement body.
        /// Either 'body' or 'ambient' must additionally be specified.
        /// </summary>
        /// <param name="name">Name of the function if applicable</param>
        /// <param name="callSignature">Signature of the lambda</param>
        /// <param name="body">Body of the lambda; may be null, in which case 'ambient' must be non-null.</param>
        /// <param name="captures">Number of captured variables.</param>
        /// <param name="locals">Number of local variables.  This number *includes* the number of lambda's formal parameters.</param>
        /// <param name="fun">May be given in place of a body statement.  In case 'body' is null, this must be non-null.</param>
        /// <param name="location">Location of the definition.</param>
        /// <param name="statistic">Function invocation statistics</param>
        internal FunctionLikeExpression(
            SymbolAtom name,
            [NotNull]CallSignature callSignature,
            Statement body,
            int captures,
            int locals,
            InvokeAmbient fun,
            LineInfo location,
            FunctionStatistic statistic)
            : base(location)
        {
            Contract.Requires(callSignature != null);
            Contract.Requires(body != null || fun != null);
            Contract.Requires(body == null || fun == null);
            Contract.Requires(captures >= 0);
            Contract.Requires(locals >= 0);
            Contract.Requires(locals >= callSignature.Parameters.Count);

            Name = name;
            CallSignature = callSignature;
            Body = body;
            m_ambient = fun;
            Captures = captures;
            Locals = locals;
            Statistic = statistic;
            Params = callSignature.Parameters.Count;
        }

        /// <summary>
        /// Creates a callable expression for the ambient function.
        /// </summary>
        /// <param name="name">Name of the function if applicable</param>
        /// <param name="callSignature">Signature of the lambda.</param>
        /// <param name="ambient">Delegate to serve as lambda's body.</param>
        /// <param name="location">Location of delegates definition.</param>
        /// <param name="statistic">Function invocation statistics.</param>
        private FunctionLikeExpression(SymbolAtom name, [NotNull]CallSignature callSignature, InvokeAmbient ambient, LineInfo location, [NotNull]FunctionStatistic statistic)
            : this(name, callSignature: callSignature, body: null, captures: 0, locals: callSignature.Parameters.Count, fun: ambient, location: location, statistic: statistic)
        {
            Contract.Requires(callSignature != null);
            Contract.Requires(ambient != null);
        }

        /// <nodoc />
        public FunctionLikeExpression(DeserializationContext context, LineInfo location)
            : base(location)
        {
            var reader = context.Reader;
            Name = reader.ReadSymbolAtom();
            CallSignature = (CallSignature)Read(context);
            Contract.Assert(CallSignature != null);

            Body = (Statement)Read(context);
            Captures = reader.ReadInt32Compact();
            Locals = reader.ReadInt32Compact();
            Params = CallSignature.Parameters.Count;
            var fullName = reader.ReadString();
            Statistic = new FunctionStatistic(fullName);
        }

        /// <inheritdoc />
        protected override void DoSerialize(BuildXLWriter writer)
        {
            writer.Write(Name);
            CallSignature.Serialize(writer);
            Serialize(Body, writer);

            Contract.Assert(m_ambient == null, "Can't serialize ambient function.");
            writer.WriteCompact(Captures);
            writer.WriteCompact(Locals);
            writer.Write(Statistic.FullName);
        }

        /// <summary>
        /// Creates ambient function.
        /// </summary>
        public static FunctionLikeExpression CreateAmbient(SymbolAtom name, CallSignature signature, InvokeAmbient fun, [NotNull]FunctionStatistic statistics)
        {
            Contract.Requires(name.IsValid);
            return new FunctionLikeExpression(name, signature, fun, default(LineInfo), statistics);
        }

        /// <summary>
        /// Creates user-defined lambda expression.
        /// </summary>
        public static FunctionLikeExpression CreateLambdaExpression(
            CallSignature callSignature,
            Statement body,
            int toCapture,
            int locals,
            LineInfo location)
        {
            return new FunctionLikeExpression(SymbolAtom.Invalid, callSignature, body, toCapture, locals, fun: null, location: location, statistic: FunctionStatistic.Empty);
        }

        /// <summary>
        /// Creates user-defined function.
        /// </summary>
        public static FunctionLikeExpression CreateFunction(
            SymbolAtom name,
            CallSignature callSignature,
            Statement body,
            int toCapture,
            int locals,
            LineInfo location,
            [NotNull]FunctionStatistic statistic)
        {
            Contract.Requires(name.IsValid);
            return new FunctionLikeExpression(name, callSignature, body, toCapture, locals, fun: null, location: location, statistic: statistic);
        }

        /// <summary>
        /// Constructs <see cref="FunctionLikeExpression"/> from <see cref="FunctionDeclaration"/>.
        /// </summary>
        public static FunctionLikeExpression CreateFunction(FunctionDeclaration function)
        {
            Contract.Requires(function != null);
            return new FunctionLikeExpression(
                function.Name,
                function.CallSignature,
                function.Body,
                function.Captures,
                function.Locals,
                fun: null,
                location: function.Location,
                statistic: function.Statistic);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        {
            visitor.Visit(this);
        }

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.LambdaExpression;

        /// <inheritdoc />
        public override string ToDebugString()
        {
            return $"{CallSignature.ToDebugString()} => {Body?.ToDebugString()}";
        }

        /// <inheritdoc/>
        public override string ToStringShort(StringTable stringTable)
        {
            if (Name.IsValid)
            {
                return I($"function '{Name.ToString(stringTable)}'");
            }

            return $"{CallSignature} => {Body?.ToDebugString()}";
        }

        /// <inheritdoc/>
        public EvaluationResult Invoke(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            SetLocalVariableNames(context);

            if (m_ambient != null)
            {
                // This is an ambient function with C# implementation.
                return m_ambient(context, env, frame);
            }

            // Evaluating body manually.
            Contract.Assert(Body != null);

            var result = Body.Eval(context, env, frame);
            if (frame.ReturnStatementWasEvaluated)
            {
                // Need to stop the evaluation if the 'return value' was already computed by the body.
                // But need to reset the return value of the stack frame, because a lambda may be called multiple times with the same stack frame.
                frame.ReturnStatementWasEvaluated = false;
                return result;
            }

            return result;
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(new Closure(env, this, frame));
        }

        private void SetLocalVariableNames(Context context)
        {
            var debugInfo = context.CallStackSize > 0 ? context.TopStack.DebugInfo : null;
            if (debugInfo != null)
            {
                for (int i = 0; i < CallSignature.Parameters.Count; i++)
                {
                    debugInfo.SetLocalVarName(i + Captures, CallSignature.Parameters[i].ParameterName);
                }
            }
        }
    }
}

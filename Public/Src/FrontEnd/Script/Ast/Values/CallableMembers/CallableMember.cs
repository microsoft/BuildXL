// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.Utilities;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Expression that represents an invocable member, i.e., member that could be bound to a value to be invoked.
    /// </summary>
    /// <remarks>
    /// TypeScript has a notion of an invocable member, i.e., members that could be used as a left hand side of an invocation
    /// expression.
    /// This class models this concept that is acts like a basic block for implementing well-known (i.e., ambient)
    /// types with some member functions like <code>path.getExtensions()</code> and similar member functions.
    /// </remarks>
    public abstract class CallableMember : Expression
    {
        /// <summary>
        /// Minimal arity of the member function
        /// </summary>
        public short MinArity { get; }

        /// <summary>
        /// Max arity of the member function
        /// </summary>
        public short MaxArity { get; }

        /// <summary>
        /// Returns true when last parameter is a rest parameter.
        /// </summary>
        public bool Rest { get; }

        /// <summary>
        /// Returns true when callable member is actually a property but not a method.
        /// </summary>
        public virtual bool IsProperty => false;

        /// <summary>
        /// Name of the callable member.
        /// </summary>
        public SymbolAtom Name { get; }

        /// <nodoc />
        public FunctionStatistic Statistic { get; }

        /// <nodoc />
        protected CallableMember(FunctionStatistic statistic, SymbolAtom name, short minArity, short maxArity, bool rest)
            : base(location: default(LineInfo))
        {
            Contract.Requires(minArity >= 0);
            Contract.Requires(maxArity >= 0);
            Contract.Requires(minArity <= maxArity);

            Name = name;
            MinArity = minArity;
            MaxArity = maxArity;
            Rest = rest;
            Statistic = statistic;
        }

        /// <summary>
        /// Creates member invocable property from delegate <paramref name="function"/>.
        /// </summary>
        public static CallableMember<T> CreateProperty<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignature0<T> function, StringTable stringTable)
        {
            return new CallableMember0<T>(new FunctionStatistic(namespaceName, name, callSignature: null, stringTable: stringTable), name, function, isProperty: true);
        }

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public static CallableMember<T> Create<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignature0<T> function, StringTable stringTable)
        {
            return new CallableMember0<T>(new FunctionStatistic(namespaceName, name, callSignature: null, stringTable: stringTable), name, function, isProperty: false);
        }

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public static CallableMember<T> Create<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignature1<T> function, StringTable stringTable, bool rest = false, short minArity = 1)
        {
            return new CallableMember1<T>(new FunctionStatistic(namespaceName, name, callSignature: null, stringTable: stringTable), name, function, minArity: minArity, rest: rest);
        }

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public static CallableMember<T> Create<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignature2<T> function, StringTable stringTable, short requiredNumberOfArguments = 2)
        {
            return new CallableMember2<T>(new FunctionStatistic(namespaceName, name, callSignature: null, stringTable: stringTable), name, function, minArity: requiredNumberOfArguments, rest: false);
        }

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public static CallableMember<T> CreateN<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignatureN<T> function, StringTable stringTable)
        {
            return new CallableMemberN<T>(new FunctionStatistic(namespaceName, name, callSignature: null, stringTable: stringTable), name, function, minArity: 0, rest: false, maxArity: short.MaxValue);
        }

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public static CallableMember<T> CreateN<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignatureN<T> function, StringTable stringTable, short minArity, short maxArity)
        {
            return new CallableMemberN<T>(new FunctionStatistic(namespaceName, name, callSignature: null, stringTable: stringTable), name, function, minArity: minArity, rest: false, maxArity: maxArity);
        }

        /// <inheritdoc />
        public override void Accept(Visitor visitor)
        { }

        /// <inheritdoc/>
        public override string ToStringShort(StringTable stringTable)
        {
            return I($"{(IsProperty ? "property" : "function")} '{Name.ToString(stringTable)}'");
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(this);
        }
    }

    /// <summary>
    /// Member function descriptor that takes T as a receiver.
    /// </summary>
    public abstract class CallableMember<T> : CallableMember
    {
        /// <nodoc />
        protected CallableMember(FunctionStatistic statistic, SymbolAtom name, short minArity, short maxArity, bool rest)
            : base(statistic, name, minArity, maxArity, rest)
        {
            Contract.Requires(minArity >= 0);
            Contract.Requires(maxArity >= 0);
            Contract.Requires(minArity <= maxArity);
        }

        /// <summary>
        /// Binds member function with a receiver to create <see cref="CallableValue"/>.
        /// </summary>
        public CallableValue<T> Bind(T receiver)
        {
            Contract.Requires(receiver != null);

            return new CallableValue<T>(receiver, this);
        }

        /// <summary>
        /// Applies a function with no argument on the <paramref name="receiver"/>.
        /// </summary>
        public virtual EvaluationResult Apply([NotNull]Context context, [NotNull]T receiver, [NotNull]EvaluationStackFrame captures)
        {
            throw new InvalidOperationException("Function with no argument is not applicable.");
        }

        /// <summary>
        /// Applies a function with one argument on the <paramref name="receiver"/>.
        /// </summary>
        public virtual EvaluationResult Apply([NotNull]Context context, [NotNull]T receiver, EvaluationResult arg, [NotNull]EvaluationStackFrame captures)
        {
            throw new InvalidOperationException("Function with 1 argument is not applicable.");
        }

        /// <summary>
        /// Applies a function with two arguments on the <paramref name="receiver"/>.
        /// </summary>
        public virtual EvaluationResult Apply([NotNull]Context context, [NotNull]T receiver, [CanBeNull]EvaluationResult arg1, [CanBeNull]EvaluationResult arg2, [NotNull]EvaluationStackFrame captures)
        {
            throw new InvalidOperationException("Function with 2 arguments is not applicable.");
        }

        /// <summary>
        /// Applies a function with N arguments on the <paramref name="receiver"/>
        /// </summary>
        public virtual EvaluationResult Apply([NotNull]Context context, [NotNull]T receiver, [CanBeNull]EvaluationResult[] args, [NotNull]EvaluationStackFrame captures)
        {
            throw new InvalidOperationException(I($"Function with {args.Length} arguments is not applicable."));
        }
    }
}

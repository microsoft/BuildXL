// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    ///     Base class for ambient namespace definition.
    /// </summary>
    /// <remarks>
    ///     In DScript there are a few ways to define a behavior:
    ///     1) by creating user-defined functions like <code>function foo() {return 42;}</code>
    ///     2) by using a well-known namespace with a free functions: <code>namespace String {function interpolate() {}</code>.
    ///     3) by using well-known interfaces with a set of member functions: <code>interface String { length: number; }</code>
    ///     For the last two cases, the prelude contains only the namespace and type declarations; behavior is defined in the interpreter.
    ///     This class is used to define a behavior for "static functions" in the namespace level and <see cref="AmbientDefinition{T}" />
    ///     is responsible for implementing behavior for well-known interfaces.
    /// </remarks>
    public abstract class AmbientDefinitionBase
    {
        /// <summary>
        ///     Types for ambients.
        /// </summary>
        protected readonly PrimitiveTypes AmbientTypes;

        /// <summary>
        /// Name of an ambient defition (like 'Array', 'Map' etc).
        /// </summary>
        protected SymbolAtom AmbientName { get; }

        /// <nodoc />
        protected StringTable StringTable => AmbientTypes.StringTable;

        /// <nodoc />
        protected internal AmbientDefinitionBase(string ambientName, PrimitiveTypes knownTypes)
        {
            Contract.Requires(knownTypes != null);

            AmbientTypes = knownTypes;
            AmbientName = ambientName == null ? SymbolAtom.Invalid : SymbolAtom.Create(knownTypes.StringTable, ambientName);
            
        }

        /// <summary>
        ///     A virtual method that subtypes should override to return all <see cref="CallableMember"/>s of this definition.
        /// </summary>
        public virtual IReadOnlyDictionary<string, CallableMember> GetCallableMembers(StringTable stringTable)
        {
            return new Dictionary<string, CallableMember>();
        }


        /// <summary>
        ///     Factory method that creates instance of the <see cref="NamespaceFunctionDefinition" />.
        /// </summary>
        protected static NamespaceFunctionDefinition Function(string name, ModuleBinding body)
        {
            return new NamespaceFunctionDefinition(name, body);
        }

        /// <summary>
        ///     Factory method that creates instance of the <see cref="NamespaceFunctionDefinition" />.
        /// </summary>
        protected NamespaceFunctionDefinition Function(string name, InvokeAmbient body, CallSignature signature)
        {
            return new NamespaceFunctionDefinition(name, CreateFun(name, body, signature));
        }

        /// <summary>
        ///     Factory method that creates instance of the <see cref="ModuleBinding"/>.
        /// </summary>
        private ModuleBinding CreateFun(string name, InvokeAmbient body, CallSignature signature)
        {
            var atomName = Symbol(name);
            var statistic = new FunctionStatistic(AmbientName, atomName, signature, StringTable);
            return ModuleBinding.CreateFun(atomName, body, signature, statistic);
        }

        /// <summary>
        /// Creates member invocable property from delegate <paramref name="function"/>.
        /// </summary>
        public CallableMember<T> CreateProperty<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignature0<T> function)
            => CallableMember.CreateProperty<T>(namespaceName, name, function, StringTable);

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public CallableMember<T> Create<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignature0<T> function)
            => CallableMember.Create<T>(namespaceName, name, function, StringTable);

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public CallableMember<T> Create<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignature1<T> function, bool rest = false, short minArity = 1)
            => CallableMember.Create<T>(namespaceName, name, function, StringTable, rest, minArity);

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public CallableMember<T> Create<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignature2<T> function, short requiredNumberOfArguments = 2)
            => CallableMember.Create<T>(namespaceName, name, function, StringTable, requiredNumberOfArguments);

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public CallableMember<T> CreateN<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignatureN<T> function)
            => CallableMember.CreateN<T>(namespaceName, name, function, StringTable);

        /// <summary>
        /// Creates member function instance from delegate <paramref name="function"/>.
        /// </summary>
        public CallableMember<T> CreateN<T>(SymbolAtom namespaceName, SymbolAtom name, CallableMemberSignatureN<T> function, short minArity, short maxArity)
            => CallableMember.CreateN<T>(namespaceName, name, function, StringTable, minArity, maxArity);

        /// <nodoc />
        protected SymbolAtom Symbol(string name)
        {
            return SymbolAtom.Create(StringTable, name);
        }

        /// <summary>
        ///     Factory method that creates instance of the <see cref="NamespaceFunctionDefinition" />.
        /// </summary>
        protected NamespaceFunctionDefinition EnumMember(string name, int value)
        {
            return new NamespaceFunctionDefinition(name, ModuleBinding.CreateEnum(Symbol(name), value));
        }

        /// <summary>
        ///     Factory method that provides definition of the ambient namespace.
        /// </summary>
        /// <remarks>
        ///     Derived type could skip this method when the type is just an interface type but not a namespace holder.
        ///     Like Number is just an interface, there is no namespace Number.
        /// </remarks>
        protected virtual AmbientNamespaceDefinition? GetNamespaceDefinition()
        {
            return null;
        }

        /// <summary>
        ///     Register set of function to the module literal (i.e., to namespace declaration).
        /// </summary>
        protected void RegisterFunctionDefinitions(ModuleLiteral module, IReadOnlyList<NamespaceFunctionDefinition> functionDefinitions)
        {
            var success = true;
            foreach (var functionDefinition in functionDefinitions)
            {
                success &= module.AddBinding(
                    Symbol(functionDefinition.Name),
                    functionDefinition.FunctionDefinition);
            }

            Contract.Assume(success);
        }

        /// <nodoc />
        protected void RegisterNamespaceDefinition(
            GlobalModuleLiteral globalModuleLiteral,
            AmbientNamespaceDefinition namespaceDefinition)
        {
            Contract.Requires(globalModuleLiteral != null);

            globalModuleLiteral.AddNamespace(
                FullSymbol.Create(globalModuleLiteral.SymbolTable, namespaceDefinition.Name),
                default(UniversalLocation), 
                null, 
                out TypeOrNamespaceModuleLiteral registeredModule);

            RegisterFunctionDefinitions(registeredModule, namespaceDefinition.FunctionDefinitions);
        }

        /// <nodoc />
        public virtual void Initialize(GlobalModuleLiteral globalModuleLiteral)
        {
            Register(globalModuleLiteral);
        }

        /// <summary>
        ///     Registers ambient to the global module literal.
        /// </summary>
        protected virtual void Register(GlobalModuleLiteral globalModuleLiteral)
        {
            Contract.Requires(globalModuleLiteral != null);
            
            var namespaceDefinition = GetNamespaceDefinition();
            if (namespaceDefinition != null)
            {
                RegisterNamespaceDefinition(globalModuleLiteral, namespaceDefinition.Value);
            }
        }

        /// <summary>
        ///     Creates required parameters for ambient signatures.
        /// </summary>
        protected static Parameter[] RequiredParameters(Type type, params Type[] types)
        {
            Contract.Requires(types != null);
            Contract.Ensures(Contract.Result<Parameter[]>() != null);

            var parameters = new Parameter[types.Length + 1];
            parameters[0] = new Parameter(type, ParameterKind.Required, default(LineInfo));
            for (var i = 0; i < types.Length; ++i)
            {
                parameters[i + 1] = new Parameter(types[i], ParameterKind.Required, default(LineInfo));
            }

            return parameters;
        }

        /// <summary>
        ///     Creates required parameters for ambient signatures.
        /// </summary>
        protected static Parameter[] RequiredParameters()
        {
            return CollectionUtilities.EmptyArray<Parameter>();
        }

        /// <summary>
        ///     Creates required parameters for ambient signatures.
        /// </summary>
        protected static Parameter[] RequiredParameters(Type type)
        {
            Contract.Requires(type != null);

            var parameters = new Parameter[1];
            parameters[0] = new Parameter(type, ParameterKind.Required, default(LineInfo));

            return parameters;
        }

        /// <summary>
        ///     Creates optional parameters for ambient signatures.
        /// </summary>
        protected static Parameter[] OptionalParameters(params Type[] types)
        {
            Contract.Requires(types != null);
            Contract.Ensures(Contract.Result<Parameter[]>() != null);

            var parameters = new Parameter[types.Length];
            for (var i = 0; i < types.Length; ++i)
            {
                parameters[i] = new Parameter(types[i], ParameterKind.Optional, default(LineInfo));
            }

            return parameters;
        }

        /// <summary>
        ///     Creates an ambient call signature.
        /// </summary>
        protected static CallSignature CreateSignature(
            Parameter[] required = null,
            Parameter[] optional = null,
            Type restParameterType = null,
            Type returnType = null)
        {
            Contract.Ensures(Contract.Result<CallSignature>() != null);

            var parameters = new List<Parameter>();

            if (required != null)
            {
                parameters.AddRange(required);
            }

            if (optional != null)
            {
                parameters.AddRange(optional);
            }

            if (restParameterType != null)
            {
                parameters.Add(new Parameter(restParameterType, ParameterKind.Rest, default(LineInfo)));
            }

            return
                new CallSignature(
                    parameters.Count == 0 ? CollectionUtilities.EmptyArray<Parameter>() : parameters.ToArray(),
                    returnType, default(LineInfo));
        }

        /// <summary>
        ///     Creates a union type.
        /// </summary>
        protected static UnionType UnionType(params Type[] types)
        {
            return new UnionType(types, default(LineInfo));
        }

         /// <summary>
        ///     Gets provenence for ambients.
        /// </summary>
        /// <remarks>
        /// The provenance is important for traceability, e.g., where object is created.
        /// This method handles the case where the ambient is a method or a property.
        /// The reason for handling both cases is for deprecation purpose, e.g., a method becomes a property.
        /// </remarks>
        protected static void GetProvenance(Context context, out AbsolutePath path, out LineInfo lineInfo)
        {
            Contract.Requires(context != null);

            path = AbsolutePath.Invalid;
            lineInfo = default(LineInfo);

            if (context.PropertyProvenance.IsValid)
            {
                // This case handles ambient as a property.
                path = context.PropertyProvenance.Path;
                lineInfo = context.PropertyProvenance.LazyLineInfo;
            }
            else if (context.CallStackSize > 0)
            {
                // This case handles ambient as a method.
                path = context.TopStack.Path;
                lineInfo = context.TopStack.InvocationLocation;
            }
        }
    }
}

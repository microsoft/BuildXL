// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Ambients
{
    /// <summary>
    /// Base class for ambient namespace and interface definition.
    /// </summary>
    /// <remarks>
    /// This class can represent both namespace level ambients and instance-level ambients.
    /// For instance, `Path` type could have two folds:
    ///
    /// Namespace level ambient functions:
    /// <code>
    /// namespace Path {
    ///   getExtension(p: Path): string;
    /// }
    /// </code>
    ///
    /// And instance level ambient functions:
    /// <code>
    /// interface Path {
    ///   getExtension(): string;
    /// }
    /// </code>
    ///
    /// First concept is modeled by the base type and second - by this one.
    /// </remarks>
    public abstract class AmbientDefinition<T> : AmbientDefinitionBase
    {
        /// <summary>
        /// Dictionary that holds a mapping from stringId to callable member.
        /// </summary>
        private Dictionary<StringId, CallableMember<T>> m_callableMembers;
        private Dictionary<int, CallableMember<T>> m_callableMembersFast;

        /// <nodoc />
        protected AmbientDefinition(string ambientName, PrimitiveTypes knownTypes)
            : base(ambientName, knownTypes)
        {
        }

        /// <summary>
        /// Resolves and returns a bound member for receiver and a specified name.
        /// </summary>
        public CallableValue<T> ResolveMember(T receiver, SymbolAtom name)
        {
            Contract.Requires(receiver != null);
            Contract.Requires(name.IsValid);

            if (m_callableMembersFast.TryGetValue(name.StringId.Value, out CallableMember<T> result))
            {
                return result.Bind(receiver);
            }

            // Member is not found!
            return null;
        }

        /// <inheritdoc/>
        public override IReadOnlyDictionary<string, CallableMember> GetCallableMembers(StringTable stringTable)
        {
            return m_callableMembers.ToDictionary(kvp => kvp.Key.ToString(stringTable), kvp => (CallableMember)kvp.Value);
        }

        /// <inheritdoc />
        public override void Initialize(GlobalModuleLiteral globalModuleLiteral)
        {
            Register(globalModuleLiteral);
            m_callableMembers = CreateMembers();

            // Every type in DScript should implement toString method.
            m_callableMembers.Add(NameId(Constants.Names.ToStringFunction), CreateToStringMember());

            m_callableMembersFast = m_callableMembers.ToDictionary(kvp => kvp.Key.Value, kvp => kvp.Value);
        }

        /// <summary>
        /// Provides a member for a <code>T.ToString()</code>.
        /// </summary>
        /// <remarks>
        /// Some types like Number has a different signature for 'toString' method.
        /// </remarks>
        protected virtual CallableMember<T> CreateToStringMember()
        {
            return Create(AmbientName, Symbol(Constants.Names.ToStringFunction), (CallableMemberSignature0<T>)this.ToStringMethod);
        }

        /// <summary>
        /// Factory method that provides members of the ambient interface.
        /// </summary>
        protected abstract Dictionary<StringId, CallableMember<T>> CreateMembers();

        /// <summary>
        /// Factory method that creates a name but returns just an identifier from the string table.
        /// </summary>
        protected StringId NameId(string name)
        {
            return Symbol(name).StringId;
        }

        /// <summary>
        /// ToString method that every type in DScript should implement.
        /// </summary>
        protected virtual EvaluationResult ToStringMethod(Context context, T receiver, EvaluationStackFrame captures)
        {
            return EvaluationResult.Create(ToStringConverter.ObjectToString(context, receiver));
        }
    }
}

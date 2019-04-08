// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Factory for creating new instance of the <see cref="IType"/>.
    /// </summary>
    public static class TypeAllocator
    {
        /// <nodoc/>
        public static T CreateType<T>(ITypeChecker checker, TypeFlags flags, ISymbol symbol) where T : IType, new()
        {
            // new() expression will use Activator.CreateInstance under the hood,
            // and this has significant performance impact.
            // Custom expression-tree-based solution preserves the same behavior but lacks performance issues.
            var type = FastActivator<T>.Create();
            type.Initialize(checker, flags, symbol);
            return type;
        }

        /// <nodoc/>
        public static TObjectType CreateObjectType<TObjectType>(ITypeChecker checker, TypeFlags kind, ISymbol symbol) where TObjectType : IObjectType, new()
        {
            return CreateType<TObjectType>(checker, kind, symbol);
        }
    }

    /// <summary>
    /// HINT: artificial base type that is helpful for managed implementation.
    /// </summary>
    public abstract class TypeBase : IType
    {
        /// <inheritdoc/>
        public TypeFlags Flags { get; set; }

        /// <inheritdoc/>
        public int Id { get; private set; }

        /// <inheritdoc/>
        public ISymbol Symbol { get; protected set; }

        /// <inheritdoc/>
        public DestructuringPattern Pattern { get; set; }

        /// <inheritdoc/>
        public void Initialize(ITypeChecker checker, TypeFlags flags, ISymbol symbol)
        {
            // Checker argument is used only by services.ts, but we're leaving it here for compatibility reasons.
            Flags = flags;

            // HINT: to simplify the code and design, this initialize method sets Id as well.
            Initialize(checker.GetNextTypeId(), flags, symbol);
        }

        /// <inheritdoc/>
        public void Initialize(int id, TypeFlags flags, ISymbol symbol)
        {
            Flags = flags;

            // HINT: to simplify the code and design, this initialize method sets Id as well.
            Id = id;
            Symbol = symbol;
        }
    }

    /// <summary>
    /// HINT: this is artificial concrete type.
    /// The only purpose is to avoid making TypeBase concrete.
    /// </summary>
    public sealed class ConcreteType : TypeBase
    { }

    /// <nodoc/>
    public sealed class IntrinsicType : TypeBase, IIntrinsicType
    {
        /// <inheritdoc/>
        public string IntrinsicName { get; set; } // Name of intrinsic type
    }

    /// <nodoc/>
    public class PredicateType : TypeBase, IPredicateType
    {
        /// <inheritdoc/>
        public ITypePredicate Predicate { get; set; }
    }

    /// <nodoc/>
    public class StringLiteralType : TypeBase, IStringLiteralType
    {
        /// <inheritdoc/>
        public string Text { get; set; }
    }

    // Object types(TypeFlags.ObjectType)

    /// <nodoc/>
    public abstract class ObjectType : TypeBase, IObjectType, IResolvedType
    {
        private ResolvedTypeData m_resolvedTypeData;
        private static readonly List<ISignature> s_emptyList = new List<ISignature>();

        // Implementation of IUnionOrIntersectionType

        /// <inheritdoc />
        List<IType> IUnionOrIntersectionType.Types { get; }

        /// <inheritdoc />
        IType IUnionOrIntersectionType.ReducedType { get; set; }

        /// <inheritdoc />
        ISymbolTable IUnionOrIntersectionType.ResolvedProperties { get; set; }

        // Implementation of IResolvedType

        /// <inheritdoc />
        ISymbolTable IResolvedType.Members => m_resolvedTypeData?.Members;

        /// <inheritdoc />
        IReadOnlyList<ISymbol> IResolvedType.Properties => m_resolvedTypeData?.Properties;

        /// <inheritdoc />
        IReadOnlyList<ISignature> IResolvedType.CallSignatures => m_resolvedTypeData?.CallSignatures ?? s_emptyList;

        /// <inheritdoc />
        IReadOnlyList<ISignature> IResolvedType.ConstructSignatures => m_resolvedTypeData?.ConstructSignatures ?? s_emptyList;

        /// <inheritdoc />
        IType IResolvedType.StringIndexType => m_resolvedTypeData?.StringIndexType;

        /// <inheritdoc />
        IType IResolvedType.NumberIndexType => m_resolvedTypeData?.NumberIndexType;

        /// <inheritdoc/>
        public IResolvedType Resolve(ResolvedTypeData resolvedTypeData)
        {
            m_resolvedTypeData = resolvedTypeData;
            return this;
        }
    }

    /// <nodoc/>
    public class InterfaceType : ObjectType, IInterfaceTypeWithDeclaredMembers, ITypeParameter
    {
        [CanBeNull]
        private InterfaceDeclaredMembersData m_declaredMembersData;

        /// <inheritdoc/>
        public IInterfaceTypeWithDeclaredMembers ResolveDeclaredMembers(InterfaceDeclaredMembersData data)
        {
            if (m_declaredMembersData != null)
            {
                return this;
            }

            lock (this)
            {
                if (m_declaredMembersData != null)
                {
                    return this;
                }

                m_declaredMembersData = data;
                return this;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<ITypeParameter> LocalTypeParameters { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<ITypeParameter> OuterTypeParameters { get; set; }

        /// <inheritdoc/>
        public IType ResolvedBaseConstructorType { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IType> ResolvedBaseTypes { get; set; }

        /// <inheritdoc/>
        public ITypeParameter ThisType { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<ITypeParameter> TypeParameters { get; set; }

        /// <inheritdoc/>
        // HINT: this is yet another hack to work around dynamic nature of typescript/javascript.
        public IReadOnlyList<ISymbol> DeclaredProperties => m_declaredMembersData?.DeclaredProperties;

        /// <inheritdoc/>
        public IReadOnlyList<ISignature> DeclaredCallSignatures => m_declaredMembersData?.DeclaredCallSignatures;

        /// <inheritdoc/>
        public IReadOnlyList<ISignature> DeclaredConstructSignatures => m_declaredMembersData?.DeclaredConstructSignatures;

        /// <inheritdoc/>
        public IType DeclaredStringIndexType => m_declaredMembersData?.DeclaredStringIndexType;

        /// <inheritdoc/>
        public IType DeclaredNumberIndexType => m_declaredMembersData?.DeclaredNumberIndexType;

        /// <inheritdoc/>
        public IType Constraint { get; set; }

        /// <inheritdoc/>
        public ITypeParameter Target { get; set; }

        /// <inheritdoc/>
        public ITypeMapper Mapper { get; set; }

        /// <inheritdoc/>
        public IType ResolvedApparentType { get; set; }
    }

    /// <nodoc/>
    public class TypeReference : ObjectType, ITypeReference, IInterfaceType
    {
        /// <inheritdoc />
        IInterfaceTypeWithDeclaredMembers IInterfaceType.ResolveDeclaredMembers(InterfaceDeclaredMembersData data)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public IGenericType Target { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IType> TypeArguments { get; internal set; }

        /// <inheritdoc />
        IReadOnlyList<ITypeParameter> IInterfaceType.TypeParameters { get; }

        /// <inheritdoc />
        IReadOnlyList<ITypeParameter> IInterfaceType.OuterTypeParameters { get; }

        /// <inheritdoc />
        IReadOnlyList<ITypeParameter> IInterfaceType.LocalTypeParameters { get; }

        /// <inheritdoc />
        ITypeParameter IInterfaceType.ThisType { get; }

        /// <inheritdoc />
        IType IInterfaceType.ResolvedBaseConstructorType { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IType> IInterfaceType.ResolvedBaseTypes { get; set; }
    }

    /// <nodoc/>
    public class TypeParameter : ObjectType, ITypeParameter, IInterfaceType
    {
        /// <inheritdoc />
        IInterfaceTypeWithDeclaredMembers IInterfaceType.ResolveDeclaredMembers(InterfaceDeclaredMembersData data)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public IType Constraint { get; set; }

        /// <inheritdoc/>
        public ITypeMapper Mapper { get; set; }

        /// <inheritdoc/>
        public IType ResolvedApparentType { get; set; }

        /// <inheritdoc/>
        public ITypeParameter Target { get; set; }

        /// <inheritdoc />
        IReadOnlyList<ITypeParameter> IInterfaceType.TypeParameters { get; }

        /// <inheritdoc />
        IReadOnlyList<ITypeParameter> IInterfaceType.OuterTypeParameters { get; }

        /// <inheritdoc />
        IReadOnlyList<ITypeParameter> IInterfaceType.LocalTypeParameters { get; }

        /// <inheritdoc />
        ITypeParameter IInterfaceType.ThisType { get; }

        /// <inheritdoc />
        IType IInterfaceType.ResolvedBaseConstructorType { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IType> IInterfaceType.ResolvedBaseTypes { get; set; }
    }

    /// <nodoc/>
    public class GenericType : ResolvedType, IGenericType, IInterfaceTypeWithDeclaredMembers, ITypeParameter
    {
        [CanBeNull]
        private InterfaceDeclaredMembersData m_declaredMembersData;

        /// <inheritdoc/>
        public IInterfaceTypeWithDeclaredMembers ResolveDeclaredMembers(InterfaceDeclaredMembersData data)
        {
            if (m_declaredMembersData != null)
            {
                return this;
            }

            lock (this)
            {
                if (m_declaredMembersData != null)
                {
                    return this;
                }

                m_declaredMembersData = data;
                return this;
            }
        }

        /// <inheritdoc/>
        public Map<ITypeReference> Instantiations { get; }

        /// <inheritdoc/>
        public IReadOnlyList<ITypeParameter> LocalTypeParameters { get; internal set; }

        /// <inheritdoc/>
        public IReadOnlyList<ITypeParameter> OuterTypeParameters { get; internal set; }

        /// <inheritdoc/>
        public IType ResolvedBaseConstructorType { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IType> ResolvedBaseTypes { get; set; }

        /// <inheritdoc/>
        IType ITypeParameter.Constraint { get; set; }

        /// <inheritdoc/>
        ITypeParameter ITypeParameter.Target { get; set; }

        /// <inheritdoc/>
        ITypeMapper ITypeParameter.Mapper { get; set; }

        /// <inheritdoc/>
        IType ITypeParameter.ResolvedApparentType { get; set; }

        /// <inheritdoc/>
        public IGenericType Target { get; internal set; }

        /// <inheritdoc/>
        public ITypeParameter ThisType { get; internal set; }

        /// <inheritdoc/>
        public IReadOnlyList<IType> TypeArguments { get; internal set; }

        /// <inheritdoc/>
        public IReadOnlyList<ITypeParameter> TypeParameters { get; internal set; }

        /// <inheritdoc/>
        public ITypeReference TypeReference { get; internal set; }

        /// <inheritdoc/>
        // HINT: this is yet another hack to work around dynamic nature of typescript/javascript.
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes", Justification = "Won't fix")]
        IReadOnlyList<ISymbol> IInterfaceTypeWithDeclaredMembers.DeclaredProperties => m_declaredMembersData?.DeclaredProperties;

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes", Justification = "Won't fix")]
        IReadOnlyList<ISignature> IInterfaceTypeWithDeclaredMembers.DeclaredCallSignatures => m_declaredMembersData?.DeclaredCallSignatures;

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes", Justification = "Won't fix")]
        IReadOnlyList<ISignature> IInterfaceTypeWithDeclaredMembers.DeclaredConstructSignatures => m_declaredMembersData?.DeclaredConstructSignatures;

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes", Justification = "Won't fix")]
        IType IInterfaceTypeWithDeclaredMembers.DeclaredStringIndexType => m_declaredMembersData?.DeclaredStringIndexType;

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes", Justification = "Won't fix")]
        IType IInterfaceTypeWithDeclaredMembers.DeclaredNumberIndexType => m_declaredMembersData?.DeclaredNumberIndexType;

        /// <nodoc/>
        public GenericType()
        {
            Instantiations = new Map<ITypeReference>();
        }
    }

    /// <nodoc/>
    public class TypePredicate : ITypePredicate
    {
        /// <inheritdoc/>
        public TypePredicateKind Kind { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public IType Type { get; set; }
    }

    /// <nodoc/>
    public class IdentifierTypePredicate : TypePredicate, IIdentifierTypePredicate
    {
        /// <inheritdoc/>
        public int? ParameterIndex { get; set; }

        /// <inheritdoc/>
        public string ParameterName { get; set; }
    }

    /// <nodoc/>
    public class ThisTypePredicate : TypePredicate, IThisTypePredicate
    { }

    /// <nodoc/>
    public class ResolvedType : ObjectType, IResolvedType, IFreshObjectLiteralType, IAnonymousType
    {
        /// <inheritdoc/>
        // HINT: this is a hack!
        // TODO: add a comment!
        IResolvedType IFreshObjectLiteralType.RegularType { get; set; }

        /// <inheritdoc/>
        IAnonymousType IAnonymousType.Target { get; set; }

        /// <inheritdoc/>
        ITypeMapper IAnonymousType.Mapper { get; set; }
    }

    /// <nodoc/>
    public class TupleType : ObjectType, ITupleType
    {
        /// <inheritdoc/>
        public IReadOnlyList<IType> ElementTypes { get; set; }
    }

    /// <nodoc/>
    public class UnionType : ObjectType, IUnionType, IResolvedType
    {
        /// <inheritdoc/>
        public IType ReducedType { get; set; }

        /// <inheritdoc/>
        public ISymbolTable ResolvedProperties { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<IType> Types { get; set; }
    }

    /// <nodoc/>
    public class IntersectionType : ObjectType, IIntersectionType, IResolvedType
    {
        /// <inheritdoc/>
        public IType ReducedType { get; set; }

        /// <inheritdoc/>
        public ISymbolTable ResolvedProperties { get; set; }

        /// <inheritdoc/>
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly", Justification = "Necessary functionality")]
        public List<IType> Types { get; set; }
    }

    /// <nodoc/>
    public class AnonymousType : ObjectType, IAnonymousType
    {
        /// <inheritdoc/>
        public ITypeMapper Mapper { get; set; }

        /// <inheritdoc/>
        public IAnonymousType Target { get; set; }
    }
}

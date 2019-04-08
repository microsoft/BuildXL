// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Workspaces;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;
using QualifierSpaceDeclaration = System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<string>>;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Helper class that is responsible for converting qualifiers.
    /// </summary>
    public sealed class V2QualifiersConverter
    {
        private static readonly ObjectPool<ConvertQualifierTypeClosure> s_convertQualifierClosures =
            new ObjectPool<ConvertQualifierTypeClosure>(
                () => new ConvertQualifierTypeClosure(),
                c => { c.Clear(); return c; });

        private readonly QualifierTable m_qualifierTable;
        private readonly ISemanticModel m_semanticModel;

        // Cache of type ids to qualifier space declarations to avoid recomputing the same type.
        // This is likely a few-writes many-reads structure, since qualifier types tend to be just a handful. So since
        // concurrent dictionary does reads in a lock-free way, this structure seems a reasonable choice.
        // For similar reasons, this structure shouldn't grow too much
        private readonly ConcurrentDictionary<int, QualifierSpaceId> m_qualifierSpaceDeclarationCache = new ConcurrentDictionary<int, QualifierSpaceId>();

        /// <nodoc />
        public V2QualifiersConverter(QualifierTable qualifierTable, ISemanticModel semanticModel)
        {
            Contract.Requires(qualifierTable != null);
            Contract.Requires(semanticModel != null);

            m_qualifierTable = qualifierTable;
            m_semanticModel = semanticModel;
        }

        /// <summary>
        /// Extracts the qualifier type from the given node and returns a corresponding qualifier space for it
        /// </summary>
        public QualifierSpaceId ConvertQualifierType(INode node)
        {
            var qualifierType = m_semanticModel.GetCurrentQualifierType(node);
            if (qualifierType == null || qualifierType.Flags == TypeFlags.Any)
            {
                // TODO: this needs to be removed once semantic evaluation will work only with new qualifiers.
                // With V1 qualifiers the condition in the if statement can be true.
                return m_qualifierTable.EmptyQualifierSpaceId;
            }

            var resolvedType = qualifierType as IResolvedType;
            if (resolvedType == null)
            {
                // This method is on a hot path, and string computation is not cheap.
                // Moving this assertion inside the loop saves reasonable amount of time for large builds.
                Contract.Assert(false, FormattableStringEx.I($"Qualifier type should be of 'IResolvedType' but got '{qualifierType.GetType()}'"));
            }

            using (var closure = s_convertQualifierClosures.GetInstance())
            {
                var func = closure.Instance.CreateClosure(resolvedType, m_qualifierTable, m_semanticModel);
                return m_qualifierSpaceDeclarationCache.GetOrAdd(resolvedType.Id, func);
            }
        }

        /// <summary>
        /// Custom closure for qualifier conversion to avoid heap allocation on each conversion.
        /// </summary>
        internal sealed class ConvertQualifierTypeClosure
        {
            private IResolvedType m_resolvedType;
            private QualifierTable m_qualifierTable;
            private ISemanticModel m_semanticModel;

            private readonly Func<int, QualifierSpaceId> m_func;

            public ConvertQualifierTypeClosure()
            {
                m_func = _ => Convert();
            }

            public Func<int, QualifierSpaceId> CreateClosure(IResolvedType resolvedType, QualifierTable qualifierTable, ISemanticModel semanticModel)
            {
                m_resolvedType = resolvedType;
                m_qualifierTable = qualifierTable;
                m_semanticModel = semanticModel;

                return m_func;
            }

            public void Clear()
            {
                m_resolvedType = null;
                m_qualifierTable = null;
                m_semanticModel = null;
            }

            public QualifierSpaceId Convert()
            {
                return CreateQualifierSpaceId(m_qualifierTable, ExtractQualifierSpaceDeclaration(m_resolvedType, m_semanticModel));
            }
        }

        private static QualifierSpaceDeclaration ExtractQualifierSpaceDeclaration(IResolvedType resolvedType, ISemanticModel semanticModel)
        {
            var result = new QualifierSpaceDeclaration();

            if (resolvedType.Properties != null)
            {
                foreach (var property in resolvedType.Properties.AsStructEnumerable())
                {
                    var propertySignature = property.ValueDeclaration.As<IPropertySignature>();
                    Contract.Assert(propertySignature != null, "Should be ensured by the linter.");

                    Contract.Assert(propertySignature.Name.Kind == TypeScript.Net.Types.SyntaxKind.Identifier, "Should be ensured by the linter");
                    string propertyName = propertySignature.Name.Text;

                    var propertyType = semanticModel.GetTypeAtLocation(propertySignature);
                    string[] propertyValues = ExtractPropertyValues(propertyType);

                    result.Add(propertyName, propertyValues);
                }
            }

            return result;
        }

        private static string[] ExtractPropertyValues(IType propertyType)
        {
            var unionType = propertyType.As<IUnionType>();
            if (unionType != null)
            {
                // Currently, qualifier only supports literals
                return unionType.Types.Select(t => t.Cast<IStringLiteralType>().Text).ToList().ToArray();
            }

            var stringLiteral = propertyType.As<IStringLiteralType>();
            Contract.Assert(stringLiteral != null, "linter");

            return new string[] { stringLiteral.Text };
        }

        private static QualifierSpaceId CreateQualifierSpaceId(QualifierTable qualifierTable, QualifierSpaceDeclaration qualifierSpaceDeclaration)
        {
            if (qualifierSpaceDeclaration.Count == 0)
            {
                return qualifierTable.EmptyQualifierSpaceId;
            }

            return qualifierTable.CreateQualifierSpace(qualifierSpaceDeclaration);
        }
    }
}

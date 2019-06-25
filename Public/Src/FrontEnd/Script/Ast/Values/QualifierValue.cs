// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Util;
using BuildXL.FrontEnd.Sdk.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using JetBrains.Annotations;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Qualifier value.
    /// </summary>
    public sealed class QualifierValue
    {
        /// <summary>
        /// Instance of unqualified value.
        /// </summary>
        public static QualifierValue Unqualified { get; } = new QualifierValue(null, QualifierId.Invalid);

        /// <summary>
        /// Object literal representing a qualifier.
        /// </summary>
        [CanBeNull]
        public ObjectLiteral Qualifier { get; }

        /// <summary>
        /// Qualifier id.
        /// </summary>
        /// <remarks>
        /// This id is useful to avoid round-trip conversions.
        /// </remarks>
        public QualifierId QualifierId { get; }

        /// <nodoc />
        internal QualifierValue(ObjectLiteral qualifier, QualifierId qualifierId)
        {
            Qualifier = qualifier;
            QualifierId = qualifierId;
        }

        internal QualifierValue(DeserializationContext context)
        {
            // The qualifier object can be null, and in that case a null node is stored
            var node = Node.Read(context);
            Qualifier = node as ObjectLiteral;

            QualifierId = context.Reader.ReadQualifierId();
        }

        internal void Serialize(BuildXLWriter writer)
        {
            // If the qualifier is null, we represent that by serializing a null node
            if (Qualifier != null)
            {
                Qualifier.Serialize(writer);
            }
            else
            {
                NullNode.Instance.Serialize(writer);
            }

            writer.Write(QualifierId);
        }

        /// <summary>
        /// Creates a qualifier value given a qualifier id.
        /// </summary>
        public static QualifierValue Create(
            QualifierId qualifierId,
            QualifierValueCache cache,
            QualifierTable qualifierTable,
            StringTable stringTable)
        {
            var result = cache.TryGet(qualifierId);
            if (result == null)
            {
                result = Create(qualifierId, qualifierTable, stringTable);
                cache.TryAdd(result);
            }

            return result;
        }

        /// <summary>
        /// Creates a qualifier value given a qualifier id.
        /// </summary>
        public static QualifierValue Create(QualifierId qualifierId, QualifierTable qualifierTable, StringTable stringTable)
        {
            Contract.Requires(qualifierId.IsValid);
            Contract.Requires(qualifierTable != null);
            Contract.Requires(qualifierTable.IsValidQualifierId(qualifierId));

            if (qualifierTable.EmptyQualifierId == qualifierId)
            {
                return CreateEmpty(qualifierTable);
            }

            Qualifier qualifier = qualifierTable.GetQualifier(qualifierId);
            var bindings = new List<Binding>(qualifier.Keys.Count);

            for (int i = 0; i < qualifier.Keys.Count; ++i)
            {
                bindings.Add(new Binding(qualifier.Keys[i], qualifier.Values[i].ToString(stringTable), location: default(LineInfo)));
            }

            return new QualifierValue(ObjectLiteral.Create(bindings, default(LineInfo), default(AbsolutePath)), qualifierId);
        }

        /// <summary>
        /// Tries create a qualifier value given an object literal.
        /// </summary>
        public static bool TryCreate(
            ImmutableContextBase context,
            ModuleLiteral env,
            object potentialLiteral,
            out QualifierValue qualifierValue,
            LineInfo lineInfo = default(LineInfo))
        {
            Contract.Requires(context != null);
            Contract.Requires(env != null);
            Contract.Requires(potentialLiteral != null);

            qualifierValue = null;

            if (potentialLiteral is ObjectLiteral0)
            {
                qualifierValue = CreateEmpty(context.FrontEndContext.QualifierTable);
                return true;
            }

            if (potentialLiteral is ObjectLiteralSlim || potentialLiteral is ObjectLiteralN)
            {
                return TryCreate(context, env, out qualifierValue, lineInfo, (ObjectLiteral)potentialLiteral);
            }

            var location = lineInfo.AsUniversalLocation(env, context);
            context.Logger.ReportQualifierMustEvaluateToObjectLiteral(context.LoggingContext, location.AsLoggingLocation(), context.GetStackTraceAsErrorMessage(location));
            return false;
        }

        private static bool TryCreate(
            ImmutableContextBase context,
            ModuleLiteral env,
            out QualifierValue qualifierValue,
            LineInfo lineInfo,
            ObjectLiteral objectLiteral)
        {
            Contract.Requires(context != null);
            Contract.Requires(env != null);
            Contract.Requires(objectLiteral != null);

            qualifierValue = null;

            if (objectLiteral.Count == 0)
            {
                qualifierValue = CreateEmpty(context.FrontEndContext.QualifierTable);
                return true;
            }

            Tuple<StringId, StringId>[] qualifierKvps = new Tuple<StringId, StringId>[objectLiteral.Count];

            int i = 0;
            foreach (var kvp in objectLiteral.Members)
            {
                if (!(kvp.Value.Value is string value))
                {
                    var location = lineInfo.AsUniversalLocation(env, context);
                    var error = new ErrorContext(name: kvp.Key, objectCtx: objectLiteral).ToErrorString(context);
                    context.Logger.ReportQualifierValueMustEvaluateToString(context.LoggingContext, location.AsLoggingLocation(), error, context.GetStackTraceAsErrorMessage(location));
                    return false;
                }

                qualifierKvps[i] = Tuple.Create(kvp.Key, context.FrontEndContext.StringTable.AddString(value));
                ++i;
            }

            QualifierId qualifierId = context.FrontEndContext.QualifierTable.CreateQualifier(qualifierKvps);
            qualifierValue = new QualifierValue(objectLiteral, qualifierId);

            return true;
        }

        /// <summary>
        /// Creates the empty qualifier value.
        /// </summary>
        public static QualifierValue CreateEmpty(QualifierTable qualifierTable)
        {
            Contract.Requires(qualifierTable != null);

            // The empty qualifier value is an empty object literal without location nor path. Since this is a runtime object, and not tied to
            // a particular declaration in a spec, this is ok
            return new QualifierValue(ObjectLiteral0.SingletonWithoutProvenance, qualifierTable.EmptyQualifierId);
        }

        /// <summary>
        /// Tries coercing qualifier.
        /// </summary>
        public bool TryCoerce(
            QualifierSpaceId targetQualifierSpaceId,
            QualifierTable qualifierTable,
            QualifierValueCache cache,
            PathTable pathTable,
            StringTable stringTable,
            LoggingContext loggingContext,
            out QualifierValue qualifierValue,
            LineInfo location,
            bool useDefaultsForCoercion,
            AbsolutePath path)
        {
            Contract.Requires(targetQualifierSpaceId.IsValid);
            Contract.Requires(qualifierTable != null);
            Contract.Requires(qualifierTable.IsValidQualifierSpaceId(targetQualifierSpaceId));
            Contract.Requires(pathTable != null);
            Contract.Requires(stringTable != null);
            Contract.Requires(loggingContext != null);
#if DEBUG
            Contract.Ensures(Contract.ValueAtReturn(out qualifierValue) == null || Contract.Result<bool>() == true, "expected 'qualifierValue' to be set to null when return value is 'false'");
            Contract.Ensures(Contract.ValueAtReturn(out qualifierValue) != null || Contract.Result<bool>() == false, "expected 'qualifierValue' to be set to non-null when return value is 'true'");
#endif
            qualifierValue = null;

            if (targetQualifierSpaceId == qualifierTable.EmptyQualifierSpaceId)
            {
                qualifierValue = CreateEmpty(qualifierTable);
                return true;
            }

            if (qualifierTable.TryCreateQualifierForQualifierSpace(
                pathTable,
                loggingContext,
                QualifierId,
                targetQualifierSpaceId,
                useDefaultsForCoercion,
                out QualifierId resultingQualifierId,
                out UnsupportedQualifierValue error))
            {
                qualifierValue = Create(resultingQualifierId, cache, qualifierTable, stringTable);
                return true;
            }

            var errorLocation = LocationData.Create(path, location.Line, location.Position);
            error.Location = errorLocation.ToLogLocation(pathTable);

            Logger.Log.ErrorUnsupportedQualifierValue(
                loggingContext,
                error.Location,
                error.QualifierKey,
                error.InvalidValue,
                error.LegalValues);

            return false;
        }

        /// <summary>
        /// Checks if this qualifier is the empty qualifier.
        /// </summary>
        public bool IsEmpty()
        {
            return ReferenceEquals(Qualifier, ObjectLiteral0.SingletonWithoutProvenance);
        }

        /// <summary>
        /// Checks if this qualifier is an unqualified qualifier.
        /// </summary>
        public bool IsUnqualified()
        {
            return !IsQualified();
        }

        /// <summary>
        /// Checks if this qualifier is an qualified qualifier.
        /// </summary>
        public bool IsQualified()
        {
            return !ReferenceEquals(this, Unqualified);
        }
    }
}

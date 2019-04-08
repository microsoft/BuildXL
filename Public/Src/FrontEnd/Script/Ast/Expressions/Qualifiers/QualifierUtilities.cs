// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using TypeScript.Net.Utilities;

namespace BuildXL.FrontEnd.Script.Expressions
{
    /// <summary>
    /// Helper class responsible for qualifiers coercion.
    /// </summary>
    internal static class QualifierUtilities
    {
        /// <summary>
        /// Coerce source qualifier with a target one.
        /// </summary>
        public static bool CoerceQualifierValue(
            Context context,
            ModuleLiteral env,
            QualifierSpaceId sourceQualifierSpaceId,
            UninstantiatedModuleInfo targetModule,
            in UniversalLocation referencingLocation,
            ObjectLiteral referencingQualifierLiteral,
            out QualifierValue referencingQualifierValue)
        {
            Contract.Requires(context != null);
            Contract.Requires(referencingQualifierLiteral != null);

            // Following code was copied with slight modifications from AmbientGlobals.ImportFrom method.
            // Unfortunately, with V1 and V2 implementation side by side we will leave with this duplication.
            // Once V1 will go away, the other implementation will be removed.
            if (!QualifierValue.TryCreate(context, env, referencingQualifierLiteral, out referencingQualifierValue, referencingLocation))
            {
                // Error has been reported.
                return false;
            }

            var targetQualifierSpaceId = targetModule.QualifierSpaceId;

            // TODO: the following check is an optimization to avoid redundant work.
            // But instead of that (or with addition to) we could have a caching layer for type coercions.
            if (sourceQualifierSpaceId != targetQualifierSpaceId)
            {
                // Do coercing only if there is a qualifier specification or mismatch qualifier spaces.
                QualifierValue originalQualifierValue = referencingQualifierValue;
                var pathTable = context.FrontEndContext.PathTable;

                if (
                    !referencingQualifierValue.TryCoerce(
                        targetQualifierSpaceId,
                        context.FrontEndContext.QualifierTable,
                        context.QualifierValueCache,
                        pathTable,
                        context.FrontEndContext.StringTable,
                        context.FrontEndContext.LoggingContext,
                        out referencingQualifierValue,
                        referencingLocation,
                        context.FrontEndHost.ShouldUseDefaultsOnCoercion(targetModule.ModuleLiteral.Path),
                        env.Id.Path))
                {
                    context.Logger.ReportQualifierCannotBeCoarcedToQualifierSpaceWithProvenance(
                        context.LoggingContext,
                        referencingLocation.AsLoggingLocation(),
                        originalQualifierValue.QualifierId.ToDisplayString(context),
                        targetQualifierSpaceId.ToDisplayString(context),
                        referencingLocation.AsLoggingLocation());
                    return false;
                }

                return true;
            }

            return true;
        }

        /// <summary>
        /// Coerces a qualifier value against a target qualifier space Id for V2 modules
        /// </summary>
        /// TODO: improve error reporting by adding the location of the qualifier type declaration of the target module
        public static bool CoerceQualifierValueForV2(
            Context context,
            QualifierValue qualifierValue,
            QualifierSpaceId sourceQualifierSpaceId,
            QualifierSpaceId targetQualifierSpaceId,
            in UniversalLocation referencingLocation,
            in UniversalLocation referencedLocation,
            out QualifierValue coercedQualifierValue)
        {
            if (sourceQualifierSpaceId != targetQualifierSpaceId)
            {
                if (!qualifierValue.TryCoerce(
                    targetQualifierSpaceId,
                    context.FrontEndContext.QualifierTable,
                    context.QualifierValueCache,
                    context.FrontEndContext.PathTable,
                    context.FrontEndContext.StringTable,
                    context.FrontEndContext.LoggingContext,
                    out coercedQualifierValue,
                    referencingLocation.AsLineInfo(),
                    useDefaultsForCoercion: false,
                    path: referencedLocation.File))
                {
                    context.Logger.ReportQualifierCannotBeCoarcedToQualifierSpaceWithProvenance(
                        context.LoggingContext,
                        referencedLocation.AsLoggingLocation(),
                        qualifierValue.QualifierId.ToDisplayString(context),
                        targetQualifierSpaceId.ToDisplayString(context),
                        referencingLocation.AsLoggingLocation());
                    return false;
                }
            }

            coercedQualifierValue = qualifierValue;
            return true;
        }
    }
}

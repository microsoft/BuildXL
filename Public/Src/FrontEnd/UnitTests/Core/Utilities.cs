// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.FrontEnd.Core
{
    internal static class Utilities
    {
        /// <summary>
        /// Generic function to convert numeric value to enum.
        /// </summary>
        /// <remarks>
        /// C# doesn't have an ability to restrict generic parameter to enums, so the only reasonable constraint is a struct constraint.
        /// </remarks>
        public static T ToEnumValue<T>(int intValue) where T : struct
        {
            // Need to convert intValue to enumerations underlying type to prevent from InvalidCastException.
            var v = Convert.ChangeType(intValue, Enum.GetUnderlyingType(typeof(T)));
            return (T)v;
        }

        /// <summary>
        /// Returns a diagnostic with a given Id.
        /// </summary>
        public static Diagnostic GetDiagnosticWithId<T>(IReadOnlyList<Diagnostic> diagnostics, T diagnosticId) where T : struct
        {
            int id = Convert.ToInt32(diagnosticId);

            if (diagnostics.All(d => d.ErrorCode != id))
            {
                string availableDiagnostics = diagnostics.Count == 0
                    ? "'empty'"
                    : string.Join(", ", diagnostics.Select(d => I($"'{d.FullMessage}'")));

                string message = I($"Can't find diagnostic '{diagnosticId}'. Known diagnostics are: {availableDiagnostics}");
                throw new InvalidOperationException(message);
            }

            return diagnostics.First(d => d.ErrorCode == id);
        }
    }
}

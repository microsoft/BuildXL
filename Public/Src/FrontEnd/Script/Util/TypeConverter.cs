// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Reflection;
using BuildXL.Utilities;
using JetBrains.Annotations;
using NotNull = JetBrains.Annotations.NotNullAttribute;

namespace BuildXL.FrontEnd.Script.Util
{
    /// <summary>
    /// Helper class responsible for type conversion.
    /// </summary>
    /// <remarks>
    /// Currently this type is used only for configuration conversion, but the idea is to use the same logic in other places, namely, ambients.
    /// </remarks>
    public static class TypeConverter
    {
        private static readonly Dictionary<RuntimeTypeHandle, Func<object, object>> s_numberConversion = new Dictionary<RuntimeTypeHandle, Func<object, object>>
        {
            [typeof(long).TypeHandle] = o => Convert.ToInt64(o, CultureInfo.InvariantCulture),
            [typeof(ulong).TypeHandle] = o => Convert.ToUInt64(o, CultureInfo.InvariantCulture),
            [typeof(int).TypeHandle] = o => Convert.ToInt32(o, CultureInfo.InvariantCulture),
            [typeof(uint).TypeHandle] = o => Convert.ToUInt32(o, CultureInfo.InvariantCulture),
            [typeof(short).TypeHandle] = o => Convert.ToInt16(o, CultureInfo.InvariantCulture),
            [typeof(ushort).TypeHandle] = o => Convert.ToUInt16(o, CultureInfo.InvariantCulture),
            [typeof(byte).TypeHandle] = o => Convert.ToByte(o, CultureInfo.InvariantCulture),
            [typeof(sbyte).TypeHandle] = o => Convert.ToSByte(o, CultureInfo.InvariantCulture),
        };

        /// <summary>
        /// Converts <paramref name="value"/> to a <see cref="AbsolutePath"/>.
        /// Returns false if the input is invalid.
        /// </summary>
        /// <remarks>
        /// Valid values for the <paramref name="value"/>:
        /// * <see cref="AbsolutePath"/>,
        /// * <see cref="BuildXL.Utilities.FileArtifact"/>,
        /// * <see cref="BuildXL.Utilities.DirectoryArtifact"/>,
        /// * <see cref="string"/> (if the conversion to AbsolutePath succeeds).
        /// </remarks>
        public static bool TryConvertAbsolutePath([NotNull] object value, [NotNull] PathTable pathTable, out AbsolutePath result)
        {
            var type = value.GetType().TypeHandle;

            if (type.IsAbsolutePath())
            {
                result = (AbsolutePath)value;
                return true;
            }

            if (type.IsFileArtifact())
            {
                result = ((FileArtifact)value).Path;
                return true;
            }

            if (type.IsDirectoryArtifact())
            {
                result = ((DirectoryArtifact)value).Path;
                return true;
            }

            var stringPath = value as string;
            if (stringPath != null)
            {
                if (AbsolutePath.TryCreate(pathTable, stringPath, out result))
                {
                    return true;
                }
            }

            result = AbsolutePath.Invalid;
            return false;
        }

        /// <summary>
        /// Converts <paramref name="value"/> to a <see cref="RelativePath"/>.
        /// Returns false if the input is invalid.
        /// </summary>
        public static bool TryConvertRelativePath([NotNull]object value, [NotNull]StringTable stringTable, out RelativePath result)
        {
            if (value is RelativePath)
            {
                result = (RelativePath)value;
                return true;
            }

            var stringValue = value as string;

            if (stringValue != null)
            {
                if (RelativePath.TryCreate(stringTable, stringValue, out result))
                {
                    return true;
                }
            }

            result = RelativePath.Invalid;
            return false;
        }

        /// <summary>
        /// Converts <paramref name="value"/> to a <see cref="PathAtom"/>.
        /// Returns false if the input is invalid.
        /// </summary>
        public static bool TryConvertPathAtom([NotNull] object value, [NotNull] StringTable stringTable, out PathAtom result)
        {
            if (value is PathAtom)
            {
                result = (PathAtom)value;
                return true;
            }

            var stringValue = value as string;
            if (stringValue != null)
            {
                if (PathAtom.TryCreate(stringTable, stringValue, out result))
                {
                    return true;
                }
            }

            result = PathAtom.Invalid;
            return false;
        }

        /// <summary>
        /// Converts <paramref name="value"/> into a number.
        /// Returns false if the input is invalid.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        [SuppressMessage("Microsoft.Design", "CA1007:UseGenericsWhereAppropriate")]
        public static bool TryConvertNumber([NotNull]object value, [NotNull]TypeInfo targetType, out object result)
        {
            Contract.Requires(value != null);
            Contract.Requires(targetType != null);
            Contract.Requires(targetType.TypeHandle.IsNumberType());

            if (targetType.IsInstanceOfType(value))
            {
                result = value;
                return true;
            }

            if (s_numberConversion.TryGetValue(targetType.TypeHandle, out Func<object, object> conversionFunc))
            {
                try
                {
                    result = conversionFunc(value);
                    return true;
                }
                catch (FormatException)
                {
                    result = null;
                    return false;
                }
            }

            result = null;
            return false;
        }

        /// <summary>
        /// Converts a given <paramref name="value"/> to a given enum type.
        /// Returns false if the input is invalid.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Enum.ToObject(System.Type,int)"/>, this method converts a numeric value to an enum value
        /// only when enum actually has it.
        /// It means, that if the enum Foo {V = 0}, the conversion of a number 1 to the Foo will return false;
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1007:UseGenericsWhereAppropriate")]
        public static bool TryConvertEnumValue(int value, [NotNull]Type targetEnumType, out object enumValue)
        {
            enumValue = null;

            try
            {
                // TODO: consider adding cashing here.
                // This method uses reflection and linear search every time.
                var values = Enum.GetValues(targetEnumType);
                foreach (var v in values)
                {
                    if (value == System.Convert.ToInt32(v, CultureInfo.InvariantCulture))
                    {
                        enumValue = v;
                        return true;
                    }
                }
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // Do nothing
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler


            return false;
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

#nullable enable

namespace BuildXL.Cache.Host.Configuration
{
    internal static class Validation
    {
        public abstract class NumericValidation : ValidationAttribute
        {
            private static readonly HashSet<Type> NumericTypes = new HashSet<Type>
            {
                typeof(int),  typeof(double),  typeof(decimal),
                typeof(long), typeof(short),   typeof(sbyte),
                typeof(byte), typeof(ulong),   typeof(ushort),
                typeof(uint), typeof(float)
            };

            protected override ValidationResult IsValid([AllowNull]object value, ValidationContext validationContext)
            {
                if (value == null)
                {
                    return Success;
                }

                // Make sure that we can handle nullable numerical types
                var type = value.GetType();
                if (!NumericTypes.Contains(Nullable.GetUnderlyingType(type) ?? type))
                {
                    return new ValidationResult($"{validationContext.DisplayName} is not of a numerical type.");
                }

                return Validate(Convert.ToDouble(value), validationContext);
            }

            protected abstract ValidationResult Validate(double value, ValidationContext validationContext);
        }

        public class RangeAttribute : NumericValidation
        {
            private readonly double _min;
            private readonly double _max;
            private readonly bool _minInclusive;
            private readonly bool _maxInclusive;

            public RangeAttribute(double min, double max, bool minInclusive = true, bool maxInclusive = true)
            {
                _min = min;
                _max = max;
                _minInclusive = minInclusive;
                _maxInclusive = maxInclusive;
            }

            protected override ValidationResult Validate(double value, ValidationContext validationContext)
            {
                return (value > _min || (_minInclusive && value == _min)) &&
                       (value < _max || (_maxInclusive && value == _max))
                    ? Success
                    : new ValidationResult($"{validationContext.DisplayName} should be in range {(_minInclusive ? "[" : "(")}{_min},{_max}{(_maxInclusive ? "]" : ")")} but its value is {value}");
            }
        }

        public class EnumAttribute : ValidationAttribute
        {
            private readonly Type _enumType;
            private readonly bool _allowNull;

            public EnumAttribute(Type enumType, bool allowNull = false)
            {
                _enumType = enumType;
                _allowNull = allowNull;
            }

            protected override ValidationResult IsValid([AllowNull]object value, ValidationContext validationContext)
            {
                if (_allowNull && value is null)
                {
                    return Success;
                }

                if (value is string s)
                {
                    if (Enum.IsDefined(_enumType, s))
                    {
                        return Success;
                    }
                }

                return new ValidationResult($"{validationContext.DisplayName} has value '{value}', which is not a valid value for enum {_enumType.FullName}");
            }
        }

        private static ValidationResult Success => ValidationResult.Success!;
    }
}

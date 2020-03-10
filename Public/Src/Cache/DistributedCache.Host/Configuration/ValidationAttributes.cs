// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

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

            protected override ValidationResult IsValid(object value, ValidationContext validationContext)
            {
                if (value == null)
                {
                    return ValidationResult.Success;
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
                    ? ValidationResult.Success
                    : new ValidationResult($"{validationContext.DisplayName} should be in range {(_minInclusive ? "[" : "(")}{_min},{_max}{(_maxInclusive ? "]" : ")")} but its value is {value}");
            }
        }

        public class EnumAttribute : ValidationAttribute
        {
            private readonly Type _enumType;

            public EnumAttribute(Type enumType)
            {
                _enumType = enumType;
            }

            protected override ValidationResult IsValid(object value, ValidationContext validationContext)
            {
                if (value is string s && Enum.IsDefined(_enumType, s))
                {
                    return ValidationResult.Success;
                }

                return new ValidationResult($"{validationContext.DisplayName} has value '{value}', which is not a valid value for enum {_enumType.FullName}");
            }
        }
    }
}

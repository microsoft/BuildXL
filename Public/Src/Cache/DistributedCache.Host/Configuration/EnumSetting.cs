// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Globalization;

namespace BuildXL.Cache.Host.Configuration
{
    /// <nodoc />
    [TypeConverter(typeof(StringConvertibleConverter))]
    public struct EnumSetting<T> : IStringConvertibleSetting
        where T : struct, System.Enum
    {
        public T Value { get; }

        public EnumSetting(T value)
        {
            Value = value;
        }

        public static implicit operator T(EnumSetting<T> value)
        {
            return value.Value;
        }

        public static implicit operator EnumSetting<T>(T value)
        {
            return new EnumSetting<T>(value);
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        public string ConvertToString()
        {
            return Value.ToString();
        }

        public object ConvertFromString(string value)
        {
            return new EnumSetting<T>((T)Enum.Parse(typeof(T), value, ignoreCase: true));
        }
    }
}

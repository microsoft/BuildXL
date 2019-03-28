// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using BuildXL.Utilities.Configuration;
using System.Reflection;
using System.IO;
using System.Linq;

namespace Test.BuildXL.Utilities
{
    public class UnsafeSandboxConfigurationTests
    {
        [Theory]
        [MemberData(nameof(TestSerializationIncludesPropertyData))]
        public void TestSerializationIncludesProperty(PropertyInfo propertyInfo, object value1, object value2)
        {
            var conf = new UnsafeSandboxConfiguration();
            propertyInfo.SetValue(conf, value1);
            var serializedBytes1 = SerializeToByteArray(conf);
            XAssert.AreEqual(value1, propertyInfo.GetValue(DeserializeFromByteArray(serializedBytes1)));

            propertyInfo.SetValue(conf, value2);
            var serializedBytes2 = SerializeToByteArray(conf);
            XAssert.AreEqual(value2, propertyInfo.GetValue(DeserializeFromByteArray(serializedBytes2)));

            // checking that serialized byte arrays are not equal ensures that the property was accounted for during serialization
            XAssert.ArrayNotEqual(serializedBytes1, serializedBytes2);
        }

        [Fact]
        public void TestIsAsSafeOrSaferThan()
        {
            var safeConf = UnsafeSandboxConfigurationExtensions.SafeDefaults;
            var unsafeConf = new UnsafeSandboxConfiguration() { MonitorFileAccesses = !safeConf.MonitorFileAccesses };
            var moreUnsafeConf = new UnsafeSandboxConfiguration() { MonitorFileAccesses = !safeConf.MonitorFileAccesses, SandboxKind = SandboxKind.None };

            // assert reflexivity
            XAssert.IsTrue(safeConf.IsAsSafeOrSaferThan(safeConf));
            XAssert.IsTrue(unsafeConf.IsAsSafeOrSaferThan(unsafeConf));
            XAssert.IsTrue(moreUnsafeConf.IsAsSafeOrSaferThan(moreUnsafeConf));

            // assert order
            XAssert.IsTrue(safeConf.IsAsSafeOrSaferThan(unsafeConf));
            XAssert.IsTrue(safeConf.IsAsSafeOrSaferThan(moreUnsafeConf));
            XAssert.IsTrue(unsafeConf.IsAsSafeOrSaferThan(moreUnsafeConf));

            // assert reverse order
            XAssert.IsFalse(unsafeConf.IsAsSafeOrSaferThan(safeConf));
            XAssert.IsFalse(moreUnsafeConf.IsAsSafeOrSaferThan(safeConf));
            XAssert.IsFalse(moreUnsafeConf.IsAsSafeOrSaferThan(unsafeConf));
        }

        [Fact]
        public void TestIsAsSafeOrSaferThanPreloaded()
        {
            var safeConf = new UnsafeSandboxConfiguration() { IgnorePreloadedDlls = false };
            var unsafeConf = new UnsafeSandboxConfiguration() { IgnorePreloadedDlls = true };

            XAssert.IsTrue(safeConf.IsAsSafeOrSaferThan(unsafeConf));
            XAssert.IsFalse(unsafeConf.IsAsSafeOrSaferThan(safeConf));
        }

        [Theory]
        [MemberData(nameof(TestSerializationIncludesPropertyData))]
        public void TestIsAsSafeOrSaferThanByProperty(PropertyInfo propertyInfo, object value1, object value2)
        {
            Func<object> failFn = () => 
            {
                XAssert.Fail($"Expected at lease one of the 2 values returned for property '{propertyInfo.Name}' to be different from the value in 'SafeDefaults'");
                return null;
            };

            var safeConf = UnsafeSandboxConfigurationExtensions.SafeDefaults;
            var safePropertyValue = propertyInfo.GetValue(safeConf);
            var unsafePropertyValue =
                !EqualityComparer<object>.Default.Equals(value1, safePropertyValue) ? value1 :
                !EqualityComparer<object>.Default.Equals(value2, safePropertyValue) ? value2 :
                failFn();

            var unsafeConf = new UnsafeSandboxConfiguration();
            propertyInfo.SetValue(unsafeConf, unsafePropertyValue);

            var msg = $"prop: '{propertyInfo.Name}'; safe value: '{safePropertyValue}'; unsafe property value: '{unsafePropertyValue}'";
            XAssert.IsTrue(safeConf.IsAsSafeOrSaferThan(unsafeConf), msg);
            XAssert.IsFalse(unsafeConf.IsAsSafeOrSaferThan(safeConf), msg);
        }

        public static IEnumerable<object[]> TestSerializationIncludesPropertyData()
        {
            var result = new List<object[]>();
            foreach (var propertyInfo in typeof(UnsafeSandboxConfiguration).GetProperties())
            {
                Type propertyType = propertyInfo.PropertyType;
                var nullableUnderlyingType = Nullable.GetUnderlyingType(propertyType);
                if (nullableUnderlyingType != null)
                {
                    result.Add(GetPropertyValues(propertyInfo, nullableUnderlyingType));
                }
                else
                {
                    result.Add(GetPropertyValues(propertyInfo, propertyType));
                }
            }

            return result;
        }

        private static object[] GetPropertyValues(PropertyInfo propertyInfo, Type propertyType)
        {
            if (propertyType == typeof(bool))
            {
                return new object[] { propertyInfo, true, false };
            }
            else if (propertyType.IsEnum)
            {
                var enumValues = propertyType.GetEnumValues();
                XAssert.IsTrue(enumValues.Length > 1, $"Enum type '{propertyType.Name}' has only one value");
                return new object[] { propertyInfo, enumValues.GetValue(0), enumValues.GetValue(1) };
            }
            else if (propertyType.IsAssignableFrom(typeof(List<string>)))
            {
                return new object[] { propertyInfo, new List<string>(1) { "str1" }, new List<string>(1) { "str2" } };
            }
            else
            {
                XAssert.Fail(
                    $"Found a property in {nameof(IUnsafeSandboxConfiguration)} ('{propertyInfo.Name}') that has a type ('{propertyType.Name}') " +
                    $"for which this test doesn't know how to pick 2 distinct values.  Please update this test to support the newly added type.");
                return new object[0];
            }
        }

        private IUnsafeSandboxConfiguration DeserializeFromByteArray(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BuildXLReader(debug: true, stream: stream, leaveOpen: true))
            {
                return UnsafeSandboxConfigurationExtensions.Deserialize(reader);
            }
        }

        private static byte[] SerializeToByteArray(UnsafeSandboxConfiguration conf)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BuildXLWriter(debug: true, stream: stream, leaveOpen: true, logStats: true))
            {
                conf.Serialize(writer);
                return stream.GetBuffer();
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Xunit;

namespace BuildXL.Cache.Host.Configuration.Test
{
    public class SettingsSerializerTests
    {
        /// <summary>
        /// QuickBuild includes settings objects which are (de)serialized by DataContractSerializers, not Newtonsoft.
        /// Some of these settings objects include DistributedContentSettings. If this test fails, then QuickBuild
        /// will fail to initialize.
        /// </summary>
        [Fact]
        public void ReadAndWriteSettings()
        {
            var dcs = DistributedContentSettings.CreateDisabled();
            TestSerializationRoundTrip(dcs);

            dcs = DistributedContentSettings.CreateEnabled(new Dictionary<string, string>(), true);
            TestSerializationRoundTrip(dcs);
        }

        [Fact]
        public void RetryIntervalForCopiesNull()
        {
            var dcs = DistributedContentSettings.CreateDisabled();
            dcs.RetryIntervalForCopiesMs = null;
            var newDcs = TestSerializationRoundTrip(dcs);
            Assert.NotNull(newDcs.RetryIntervalForCopiesMs);
            Assert.Equal(DistributedContentSettings.DefaultRetryIntervalForCopiesMs.Length, newDcs.RetryIntervalForCopiesMs.Length);
        }

        [Fact]
        public void AllPublicPropertiesShouldBeMarkedWithDataMemberAttributes()
        {
            var type = typeof(DistributedContentSettings);
            foreach (var property in type.GetProperties(BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public))
            {
                // Checking only read-write properties, because it is possible to have get-only properties that are not serializable.
                if (property.CanRead && property.CanWrite)
                {
                    var attribute = property.CustomAttributes.FirstOrDefault(a => a.AttributeType == typeof(DataMemberAttribute));
                    if (attribute == null)
                    {
                        throw new Exception($"Property '{property.Name}' is not marked with 'DataMemberAttribute'.");
                    }
                }
            }
        }

        private DistributedContentSettings TestSerializationRoundTrip(DistributedContentSettings dcs)
        {
            using (var stream = new MemoryStream())
            {
                var ser = new DataContractSerializer(typeof(DistributedContentSettings));
                ser.WriteObject(stream, dcs);

                stream.Seek(0, SeekOrigin.Begin);
                return (DistributedContentSettings)ser.ReadObject(stream);
            }
        }
    }
}

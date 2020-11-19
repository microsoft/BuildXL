// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using BuildXL.Cache.Host.Service;
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

            dcs = DistributedContentSettings.CreateEnabled();
            TestSerializationRoundTrip(dcs);
        }

        [Fact]
        public void RetryIntervalForCopiesNull()
        {
            var dcs = DistributedContentSettings.CreateDisabled();
            dcs.RetryIntervalForCopiesMs = null;
            var newDcs = TestSerializationRoundTrip(dcs);
            Assert.NotNull(newDcs.RetryIntervalForCopies);
            Assert.Equal(DistributedContentSettings.DefaultRetryIntervalForCopiesMs.Length, newDcs.RetryIntervalForCopies.Count);
        }

        [Fact]
        public void RetryIntervalForCopiesNullJson()
        {
            var dcs = DistributedContentSettings.CreateDisabled();
            dcs.RetryIntervalForCopiesMs = null;
            var newDcs = TestJsonSerializationRoundTrip(dcs);
            Assert.NotNull(newDcs.RetryIntervalForCopies);
            Assert.Equal(DistributedContentSettings.DefaultRetryIntervalForCopiesMs.Length, newDcs.RetryIntervalForCopies.Count);
        }

        [Fact]
        public void RetryIntervalForCopiesCustomJson()
        {
            int count = 100;
            var dcs = DistributedContentSettings.CreateDisabled();
            dcs.RetryIntervalForCopiesMs = Enumerable.Range(0, count).ToArray();
            var newDcs = TestJsonSerializationRoundTrip(dcs);
            Assert.NotNull(newDcs.RetryIntervalForCopies);
            Assert.Equal(count, newDcs.RetryIntervalForCopies.Count);
        }

        [Fact]
        public void NonDataContractsMemberIsDeserialized()
        {
            var dcs = DistributedContentSettings.CreateDisabled();
            dcs.GrpcCopyClientGrpcCoreClientOptions = new ContentStore.Grpc.GrpcCoreClientOptions() {
                MaxReconnectBackoffMs = 120,
            };

            var newDcs = TestSerializationRoundTrip(dcs);
            Assert.NotNull(newDcs.GrpcCopyClientGrpcCoreClientOptions);
            Assert.Equal(dcs.GrpcCopyClientGrpcCoreClientOptions.MaxReconnectBackoffMs, newDcs.GrpcCopyClientGrpcCoreClientOptions.MaxReconnectBackoffMs);
        }

        [Fact]
        public void AllPublicPropertiesShouldBeMarkedWithDataMemberAttributes()
        {
            // There are several weird things about serialization/deserialization:
            //  1. You need to mark all things you want to serialize with [DataMember], because the type is marked with [DataContract].
            //  2. You DO NOT need to mark recursive types with neither [DataMember] nor [DataContract]. They will get serialized/deserialized accordingly. The property itself must still be marked with [DataMember].
            //  3. Failure to declare [DataMember] will cause the property to be default-initialized, without running the constructor.
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

        private DistributedContentSettings TestJsonSerializationRoundTrip(DistributedContentSettings dcs)
        {
            var serialized = JsonSerializer.Serialize(dcs, DeploymentUtilities.ConfigurationSerializationOptions);
            return JsonSerializer.Deserialize<DistributedContentSettings>(serialized, DeploymentUtilities.ConfigurationSerializationOptions);
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

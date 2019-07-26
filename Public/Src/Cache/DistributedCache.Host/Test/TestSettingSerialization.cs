// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
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

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace BuildXL.Cache.Host.Configuration
{
    [DataContract]
    public class RedisContentSecretNames
    {
        [JsonConstructor]
        public RedisContentSecretNames(string redisContentSecretName, string redisMachineLocationsSecretName)
        {
            RedisContentSecretName = redisContentSecretName;
            RedisMachineLocationsSecretName = redisMachineLocationsSecretName;
        }

        public RedisContentSecretNames(string redisSecretName)
            : this(redisSecretName, redisSecretName)
        {
        }

        /// <summary>
        /// Secret used to connect to a Redis containing content.
        /// </summary>
        [DataMember]
        public string RedisContentSecretName { get; private set; }

        /// <summary>
        /// Secret used to connect to a Redis containing machine locations.
        /// </summary>
        [DataMember]
        public string RedisMachineLocationsSecretName { get; private set; }

        [DataMember]
        public string KeyVaultSecretName { get; set; }
    }
}

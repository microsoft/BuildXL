// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;


namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Printing different types of configuration into a json indented format
    /// Mask property values that contain sensitive information, like credentials for connecting to redis.
    /// </summary>
    public static class ConfigurationPrinter
    {
        /// <summary>
        /// We check all property names to contain any element from this list of strings.
        /// If a string is contained in the property name, we deem the property are sensitive information, and mask the property value.
        /// Note: If property name gets changed, this list needs to be updated accordingly.
        /// </summary>
        public static string[] CheckSensitiveProperties = new string[] { "ConnectionString", "Credentials" };

        /// <nodoc />
        public static string ConfigToString<T>(T config, bool withSecrets = false)
        {
            if (withSecrets)
            {
                return JsonConvert.SerializeObject(config, Formatting.Indented);
            }

            var jsonSettings = new JsonSerializerSettings()
                               {
                                   ContractResolver = new MaskPropertiesResolver(CheckSensitiveProperties),
                                   Converters = new[] {new AbsolutePathConverter()}
                               };
            return JsonConvert.SerializeObject(config, Formatting.Indented, jsonSettings);
        }

        /// <nodoc />
        public static void TraceConfiguration<T>(T config, ILogger logger)
        {
            logger.Debug($"JSON serialized of {typeof(T)}: {ConfigToString(config) }");
        }
    }

    internal class MaskPropertiesResolver : DefaultContractResolver
    {
        private readonly IEnumerable<string> _propsToMask;

        /// <nodoc />
        public MaskPropertiesResolver(IEnumerable<string> propNamesToMask)
        {
            _propsToMask = propNamesToMask;
        }

        /// <summary>
        /// If a property name contains strings we mark as secretive, we mask the property value.
        /// </summary>
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);
            PropertyInfo propValue = member as PropertyInfo;
            foreach (string sensitiveProperty in _propsToMask)
            {
                if (property.PropertyName.Contains(sensitiveProperty))
                {
                    property.ValueProvider = new MaskValueProvider(propValue, maskValue: "XXX");
                }
            }
            return property;
        }
    }

    internal class MaskValueProvider : IValueProvider
    {
        private readonly PropertyInfo _targetProperty;
        private readonly string _maskValue;

        /// <nodoc />
        public MaskValueProvider(PropertyInfo targetProperty, string maskValue)
        {
            _targetProperty = targetProperty;
            _maskValue = maskValue;
        }

        /// <nodoc />
        public void SetValue(object target, object value)
        {
            _targetProperty.SetValue(target, value);
        }

        /// <nodoc />
        public object GetValue(object target)
        {
            return _maskValue;
        }
    }

    internal class AbsolutePathConverter : JsonConverter
    {
        /// <summary>
        /// For AbsolutePath type objects we only want to print the path value and no other properties
        /// </summary>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var path = (AbsolutePath) value ;
            serializer.Serialize(writer, path.ToString());
        }

        /// <nodoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        /// <nodoc />
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(AbsolutePath);
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Notifications;

namespace BuildXL.Cache.Monitor.Library.Notifications
{
    internal class LogNotifier<T> : INotifier<T>
    {
        private readonly ILogger _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public LogNotifier(ILogger logger)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions() { WriteIndented = true };

            // Ensures that we prettify enum values when printing to console
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());

            // Ensures that we prettify TimeSpan values when printing to console
            _jsonOptions.Converters.Add(new TimeSpanConverter());
        }

        public void Emit(T notification) => _logger.Always(JsonSerializer.Serialize(notification, _jsonOptions));

        /// <summary>
        /// See: https://github.com/dotnet/runtime/issues/29932#issuecomment-540200476
        /// </summary>
        private class TimeSpanConverter : JsonConverter<TimeSpan>
        {
            public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return TimeSpan.Parse(reader.GetString());
            }

            public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }
    }
}

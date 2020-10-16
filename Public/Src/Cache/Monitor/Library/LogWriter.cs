// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Monitor.App.Notifications;

namespace BuildXL.Cache.Monitor.App
{
    internal class LogWriter<T> : INotifier<T>
    {
        private readonly ILogger _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public LogWriter(ILogger logger)
        {
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions() { WriteIndented = true };
            _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public void Emit(T notification) => _logger.Always(JsonSerializer.Serialize(notification, _jsonOptions));
    }
}

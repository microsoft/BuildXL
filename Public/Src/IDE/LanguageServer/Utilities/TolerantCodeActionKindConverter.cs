// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BuildXL.Ide.LanguageServer.Utilities
{
    /// <summary>
    /// A converter for <see cref="CodeActionKind"/> that maps unknown values to <see cref="CodeActionKind.Empty"/>
    /// instead of throwing. VS Code may send code action kinds registered by other extensions
    /// (e.g., "source.fixAll.eslint") that are not part of the LSP protocol enum.
    /// </summary>
    internal sealed class TolerantCodeActionKindConverter : StringEnumConverter
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type objectType)
        {
            Type underlying = Nullable.GetUnderlyingType(objectType) ?? objectType;
            return underlying == typeof(CodeActionKind);
        }

        /// <inheritdoc/>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                return base.ReadJson(reader, objectType, existingValue, serializer);
            }
            catch (JsonSerializationException)
            {
                return CodeActionKind.Empty;
            }
        }
    }
}

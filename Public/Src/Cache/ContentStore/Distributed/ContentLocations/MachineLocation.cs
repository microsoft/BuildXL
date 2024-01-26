// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Distributed;

#nullable enable

/// <summary>
/// This class handles serialization and deserialization of <see cref="MachineLocation"/> from JSON.
/// </summary>
/// <remarks>
/// It is important to declare this as a <see cref="JsonConverter{T}"/> because it is the only .NET 4.7.2 compatible
/// mechanism to do so.
/// </remarks>
public class MachineLocationJsonConverter : JsonConverter<MachineLocation>
{
    public override MachineLocation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var document = JsonDocument.ParseValue(ref reader);

            if (document.RootElement.TryGetProperty(nameof(MachineLocation.Uri), out var uriProperty) && uriProperty.ValueKind == JsonValueKind.String)
            {
                return MachineLocation.Parse(uriProperty.GetString()!);
            }

            if (document.RootElement.TryGetProperty(nameof(MachineLocation.Path), out var pathProperty) && pathProperty.ValueKind == JsonValueKind.String)
            {
                return MachineLocation.Parse(pathProperty.GetString()!);
            }

            return default;
        }

        var data = reader.GetString();
        return data == null ? default : MachineLocation.Parse(data);
    }

    public override void Write(Utf8JsonWriter writer, MachineLocation value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>
/// This represents the location of a CAS. This can be essentially any arbitrary URI. In practice, this is used:
/// 1. To represent hostnames
/// 2. To represent specific paths on a host. This can be:
///     - The local host, in which case the URI is to a directory (ex: file:\\C:\path\to\cas\root)
///     - A remote host, in which case the URI can be:
///       - A UNC path (ex: \\host\path\to\cas\root)
///       - In Linux and Mac OS, a UNC-style path (ex: /host/path/to/cas/root)
/// 
/// There's a few complexities here:
/// 1. The MachineLocation has to be able to accept an Invalid state, because this happens in several places. It is
///    represented as having the Uri be null. This can be instantiated by either default-initializing the struct or
///    creating a new instance with uri: string.Empty.
/// 2. When the MachineLocation is invalid, Path is the empty string.
/// 3. When the MachineLocation is valid, Path is expected to point to either:
///     - A fully qualified path to a file across the network (ex: \\host\path\to\cas\root)
///     - An absolute path to a file on the local machine (ex: C:\path\to\cas\root)
///     - A URI to a remote hostname (ex: grpc://host:1234/)
/// </summary>
[JsonConverter(typeof(MachineLocationJsonConverter))]
public readonly record struct MachineLocation
{
    public static MachineLocation Invalid { get; } = new(null);

    /// <summary>
    /// Gets whether the current machine location represents valid data
    /// </summary>
    [MemberNotNullWhen(true, nameof(Uri))]
    public bool IsValid => Uri is not null;

    /// <summary>
    /// Extracts the path from the URI.
    /// </summary>
    /// <remarks>
    /// This should only be used by tests.
    /// </remarks>
    public string Path
    {
        get
        {
            if (!IsValid)
            {
                return string.Empty;
            }

            if (Uri.IsFile && !Uri.IsUnc)
            {
                if (!OperatingSystemHelper.IsWindowsOS)
                {
                    var path = new AbsolutePath(Uri.OriginalString);
                    Contract.Assert(path.IsLocal);

                    var segments = path.GetSegments();
                    Contract.Assert(segments.Count >= 1);

                    return System.IO.Path.Combine(segments.Skip(1).ToArray());
                }

                return Uri.LocalPath;
            }

            return Uri.ToString();
        }
    }

    /// <summary>
    /// Uri that represents the actual machine location.
    /// </summary>
    /// <remarks>
    /// This can be null when the machine location is invalid. This happens only if the MachineLocation is default
    /// initialized.
    /// </remarks>
    internal Uri? Uri { get; private init; }

    private static readonly Regex _hostRegex = new(@"^(?<host>([A-Za-z0-9]+(-|\.))*[A-Za-z0-9]+)(:(?<port>\d+))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <nodoc />
    private MachineLocation(Uri? uri)
    {
        Uri = uri;
    }

    /// <nodoc />
    public static MachineLocation Create(string machineName, int port)
    {
        if (string.IsNullOrEmpty(machineName))
        {
            return Invalid;
        }

        return new MachineLocation(new Uri($"grpc://{machineName}:{port}/"));
    }

    /// <nodoc />
    public static MachineLocation FromContainerPath(AbsoluteContainerPath path)
    {
        return new MachineLocation(new Uri($"azs://{path.Account}/{path.Container}"));
    }

    /// <nodoc />
    public static MachineLocation Parse(string uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return Invalid;
        }

        // hostname:1234 is a valid URI, equivalent to hostname://:1234. This is contrary to our convention, so we
        // do this horrible thing where we try to convert this into a URI we agree with.
        Uri url;
        try
        {
            var match = _hostRegex.Match(uri);
            if (match.Success)
            {
                url = new Uri($"grpc://{uri}/");
            }
            else
            {
                url = new Uri(uri);
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to extract {nameof(MachineLocation)} from {uri}", nameof(uri), ex);
        }

        return new MachineLocation(url);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Uri?.OriginalString ?? Uri?.ToString() ?? string.Empty;
    }

    /// <nodoc />
    public (string host, int? port) ExtractHostInfo()
    {
        Contract.Requires(IsValid, $"Attempt to obtain Host and Port from invalid {nameof(MachineLocation)}");

        // The following if statement is meant to find out URIs of the form file://<blah>, so we explicitly exclude UNC
        // paths.
        if (Uri.IsFile && !Uri.IsUnc)
        {
            if (!OperatingSystemHelper.IsWindowsOS)
            {
                var path = new AbsolutePath(Uri.OriginalString);
                Contract.Assert(path.IsLocal);

                var segments = path.GetSegments();
                Contract.Assert(segments.Count >= 1);

                return (segments[0], null);
            }

            return ("localhost", null);
        }

        // Port is reported as -1 when unavailable. We prefer to use null for historical reasons. This code-path should
        // almost never be executed in production because we always specify ports.
        int? port = Uri.Port;
        if (port is < 0 or > 65535)
        {
            port = null;
        }

        return (Uri.Host, port);
    }
}

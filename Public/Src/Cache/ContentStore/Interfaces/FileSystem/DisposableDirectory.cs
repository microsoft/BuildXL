// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem;

/// <summary>
/// A temp directory that is recursively deleted on disposal
/// </summary>
public sealed class DisposableDirectory : IDisposable
{
    private readonly IAbsFileSystem _fileSystem;

    /// <summary>
    /// Gets path to the directory
    /// </summary>
    public AbsolutePath Path { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DisposableDirectory" /> class.
    /// </summary>
    public DisposableDirectory(IAbsFileSystem fileSystem, AbsolutePath? directoryPath = null)
    {
        Contract.RequiresNotNull(fileSystem);

        // We do things under the CloudStore folder to ensure that we don't accidentally clash with anything else
        directoryPath ??= fileSystem.GetTempPath() / "CloudStore" / AbsolutePath.CreateRandomName();

        _fileSystem = fileSystem;
        Path = directoryPath;
        fileSystem.CreateDirectory(Path);
    }

    /// <summary>
    /// Create path to a randomly named file inside this directory.
    /// </summary>
    public AbsolutePath CreateRandomFileName()
    {
        return AbsolutePath.CreateRandomFileName(Path);
    }

    /// <summary>
    /// Creates a randomly named file inside this directory that will be deleted on disposal.
    /// </summary>
    public DisposableFile CreateTemporaryFile(Context context)
    {
        return new DisposableFile(context, _fileSystem, CreateRandomFileName());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (_fileSystem.DirectoryExists(Path))
            {
                _fileSystem.DeleteDirectory(Path, DeleteOptions.All);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine("Unable to cleanup due to exception: {0}", exception);
        }
    }
}

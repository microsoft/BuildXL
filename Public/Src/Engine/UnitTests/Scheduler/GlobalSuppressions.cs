// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable SA1404 // Code analysis suppression should have justification

// PipExecutorTests.cs uses File.WriteAllText and File.ReadAllText in many methods, so instead of copying and pasting it many times for
// every method, I am setting a global suppression
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02", 
    Justification = "ReadAllText and WriteAllText have async versions in .NET Standard which cannot be used in full framework.")]

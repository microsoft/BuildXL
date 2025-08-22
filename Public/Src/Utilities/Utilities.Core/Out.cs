// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

#nullable enable

namespace BuildXL.Utilities;

/// <summary>
/// Helpers class for syntactic sugar for more fluid coding
/// </summary>
public static class Out
{
    /// <summary>
    /// Invokes the function and returns the result
    /// </summary>
    public static T Invoke<T>(Func<T> invoke)
    {
        return invoke();
    }

    /// <summary>
    /// Returns the value as an out parameter and return value
    /// </summary>
    public static T Var<T>(out T value, T input)
    {
        value = input;
        return value;
    }
}
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Special marker attribute used by ErrorProne.NET analyzer to warn when <see cref="System.Threading.Tasks.Task.ConfigureAwait"/> method is used.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Assembly)]
public class DoNotUseConfigureAwaitAttribute : System.Attribute { }

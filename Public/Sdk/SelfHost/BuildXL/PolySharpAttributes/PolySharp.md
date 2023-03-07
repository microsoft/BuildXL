# What is it?

The `PolySharpAttributes` folder contains the polyfills for C# language features to use all runtime-agnostic language features regardless of the target framework.

# What is this for?
If you want to use the latest C# features like non-nullable types, records, interpolated string improvements etc and target various framework you have to manually add special attributes to the compilation.

The BuildXL SDK does it automatically by adding to every project set a list of attributes that doesn't exist in the specific target. For instance, non-nullable attributes available only in .NET Core but you can use them with full framework if you add `AllowNull`, `MaybeNull` etc attributes manually, so the SDK will add such attributes for .NET Framework or .NET Standard and will do nothing for .NET Core. The same is true for other language features like records, init-only properties, interpolated string improvements, required memebers etc.

**PolySharp** includes the following polyfills:
- Nullability attributes (for [nullable reference types](https://learn.microsoft.com/dotnet/csharp/nullable-references)):
  - `[AllowNull]`
  - `[DisallowNull]`
  - `[DoesNotReturn]`
  - `[DoesNotReturnIf]`
  - `[MaybeNull]`
  - `[MaybeNullWhen]`
  - `[MemberNotNull]`
  - `[MemberNotNullWhen]`
  - `[NotNull]`
  - `[NotNullIfNotNull]`
  - `[NotNullWhen]`
- `[UnscopedRef]` (see [low-level struct improvements](https://github.com/dotnet/csharplang/blob/main/proposals/low-level-struct-improvements.md))
- Required members (see [required modifier](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/required))
  - `[RequiredMember]`
  - `[SetsRequiredMembers]`
- `[CompilerFeatureRequired]` (needed to support several features)
- `[IsExternalInit]` (needed for [init-only properties](https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/init))
- `[SkipLocalsInit]` (see [docs](https://learn.microsoft.com/dotnet/csharp/language-reference/attributes/general#skiplocalsinit-attribute))
- Interpolated string handlers (see [docs](https://learn.microsoft.com/dotnet/csharp/whats-new/tutorials/interpolated-string-handler))
  - `[InterpolatedStringHandler]`
  - `[InterpolatedStringHandlerArgument]`
- `[CallerArgumentExpression]` (see [docs](https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-10.0/caller-argument-expression))
- `[RequiresPreviewFeatures]` (needed for [preview features](https://github.com/dotnet/designs/blob/main/accepted/2021/preview-features/preview-features.md))
- `[StringSyntax]` (needed to enable [syntax highlight in the IDE](https://github.com/dotnet/runtime/issues/62505))
- `[ModuleInitializer]` (needed to enable [custom module initializers](https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-9.0/module-initializers))
- `[StackTraceHidden]` (allows hiding a stack trace from a string representation of a stacktrace)

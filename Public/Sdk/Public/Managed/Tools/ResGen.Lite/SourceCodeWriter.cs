// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Editing;

namespace ResGen.Lite
{
    /// <summary>
    /// Class for strongly typed writer
    /// </summary>
    public static class SourceCodeWriter
    {
        private static readonly AssemblyName s_toolAssemblyName = typeof(SourceCodeWriter).GetTypeInfo().Assembly.GetName();

        /// <summary>
        /// Writes a strongly typed helper class for the
        /// </summary>
        public static void Write(string filePath, ResourceData data, string namespaceName, string className, bool isPublic, string languageName)
        {
            var sourceFile = CreateSourceFile(data, namespaceName, className, isPublic, languageName);

            try
            {
                // TextWriter from System.IO has problems unifying versions from nuget and BCL between net461, net472 and netcoreapp3.0.
                // So using ToFullString instead of WriteTo.
                var sourceFileText = sourceFile.NormalizeWhitespace().ToFullString();
                File.WriteAllText(filePath, sourceFileText, Encoding.UTF8);
            }
            catch (Exception e) when
                (e is IOException || e is UnauthorizedAccessException)
            {
                throw new ResGenLiteException(
                    $"{filePath}: Error: Error writing resources file: {e.Message}");
            }
        }

        /// <nodoc />
        private static SyntaxNode CreateSourceFile(ResourceData data, string namespaceName, string className, bool isPublic, string languageName)
        {
            var workspace = new AdhocWorkspace();
            var generator = SyntaxGenerator.GetGenerator(workspace, languageName);

            var accessibility = isPublic ? Accessibility.Public : Accessibility.Internal;
            var resourceManagerType = generator.IdentifierName("global::System.Resources.ResourceManager");
            var cultureInfoType = generator.IdentifierName("global::System.Globalization.CultureInfo");

            var stringClassMember = new List<SyntaxNode>()
            {
                generator.FieldDeclaration(
                    name: "resourceMan",
                    type: resourceManagerType,
                    accessibility: Accessibility.Private,
                    modifiers: DeclarationModifiers.Static
                ),

                generator.FieldDeclaration(
                    name: "resourceCulture",
                    type: cultureInfoType,
                    accessibility: Accessibility.Private,
                    modifiers: DeclarationModifiers.Static
                ),

                generator.AddAttributes(
                    generator.PropertyDeclaration(
                        name: "ResourceManager",
                        type: resourceManagerType,
                        accessibility: accessibility,
                        modifiers: DeclarationModifiers.ReadOnly | DeclarationModifiers.Static,
                        getAccessorStatements: new SyntaxNode[]
                        {
                            generator.IfStatement(
                                condition: generator.InvocationExpression(
                                    generator.MemberAccessExpression(
                                        generator.IdentifierName("global::System.Object"),
                                        generator.IdentifierName("ReferenceEquals")),
                                    generator.IdentifierName("resourceMan"),
                                    generator.NullLiteralExpression()
                                ),
                                trueStatements: new SyntaxNode[]
                                {
                                    generator.LocalDeclarationStatement(
                                        resourceManagerType,
                                        "temp",
                                        generator.ObjectCreationExpression(
                                            resourceManagerType,
                                            generator.LiteralExpression(string.IsNullOrEmpty(namespaceName) ? className : $"{namespaceName}.{className}"),
                                            generator.MemberAccessExpression(
                                                generator.TypeOfExpression(generator.IdentifierName(className)),
                                                "Assembly"
                                            )
                                        )
                                    ),
                                    generator.AssignmentStatement(
                                        generator.IdentifierName("resourceMan"),
                                        generator.IdentifierName("temp")
                                    )
                                }
                            ),
                            generator.ReturnStatement(generator.IdentifierName("resourceMan")),
                        }
                    ),
                    GetEditorBrowsableAttribute(generator)
                ).WithLeadingTrivia(
                    GetDocComment("Returns the cached ResourceManager instance used by this class.")),

                generator.AddAttributes(
                    generator.PropertyDeclaration(
                        name: "Culture",
                        type: cultureInfoType,
                        accessibility: accessibility,
                        modifiers: DeclarationModifiers.Static,
                        getAccessorStatements: new SyntaxNode[]
                        {
                            generator.ReturnStatement(generator.IdentifierName("resourceCulture")),
                        },
                        setAccessorStatements: new SyntaxNode[]
                        {
                            generator.AssignmentStatement(
                                generator.IdentifierName("resourceCulture"),
                                generator.IdentifierName("value")
                            )
                        }
                    ),
                    GetEditorBrowsableAttribute(generator)
                ).WithLeadingTrivia(
                    GetDocComment("Overrides the current thread's CurrentUICulture property for all resource lookups using this strongly typed resource class.")),
            };

            foreach (var stringValue in data.StringValues)
            {
                var stringProperty = generator.PropertyDeclaration(
                    name: stringValue.Name,
                    type: generator.TypeExpression(SpecialType.System_String),
                    accessibility: accessibility,
                    modifiers: DeclarationModifiers.ReadOnly | DeclarationModifiers.Static,
                    getAccessorStatements: new SyntaxNode[]
                    {
                        generator.ReturnStatement(
                            generator.InvocationExpression(
                                generator.MemberAccessExpression(
                                    generator.IdentifierName("ResourceManager"),
                                    "GetString"
                                    ),
                                generator.LiteralExpression(stringValue.Name),
                                generator.IdentifierName("resourceCulture")
                            )
                        )
                    }
                ).WithLeadingTrivia(GetDocComment(stringValue.Comment ?? stringValue.Value));

                stringClassMember.Add(stringProperty);
            }

            var generatedClass = generator.AddAttributes(
                generator.ClassDeclaration(
                    className,
                    accessibility: accessibility,
                    modifiers: DeclarationModifiers.ReadOnly | DeclarationModifiers.Static,
                    members: stringClassMember
                ).WithLeadingTrivia(GetDocComment("A strongly-typed resource class, for looking up localized strings, etc.")),
                generator.Attribute("global::System.CodeDom.Compiler.GeneratedCodeAttribute",
                    generator.LiteralExpression(s_toolAssemblyName.Name),
                    generator.LiteralExpression(s_toolAssemblyName.Version.ToString())
                ),
                generator.Attribute("global::System.Diagnostics.DebuggerNonUserCodeAttribute()"),
                generator.Attribute("global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()")
            );

            var compilationUnit = string.IsNullOrEmpty(namespaceName)
                ? generator.CompilationUnit(
                    generator.NamespaceImportDeclaration("System"),
                    generatedClass
                )
                : generator.CompilationUnit(
                    generator.NamespaceDeclaration(namespaceName,
                        generator.NamespaceImportDeclaration("System"),
                        generatedClass)
                );

            return compilationUnit.WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia($@"
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by: {s_toolAssemblyName.Name} version: {s_toolAssemblyName.Version}
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
")
                );
        }

        private static SyntaxNode GetEditorBrowsableAttribute(SyntaxGenerator generator)
        {
            return generator.Attribute(
                "global::System.ComponentModel.EditorBrowsableAttribute",
                generator.MemberAccessExpression(
                    generator.IdentifierName("global::System.ComponentModel.EditorBrowsableState"),
                    "Advanced"
                )
            );
        }

        /// <summary>
        ///  Generate a summary tag based doc comment
        /// </summary>
        /// <remarks>
        /// This code uses ParseLeadingTrivia and xml encodes the summary text manually because
        /// the AST for doc comment trivia is way too complicated to create by hand.
        /// </remarks>
        private static SyntaxTriviaList GetDocComment(string summary)
        {
            var encodedLines = summary
                .Trim()
                .Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(trimmedLine => !string.IsNullOrEmpty(trimmedLine))
                .Select(trimmedLine => new XText(trimmedLine).ToString(SaveOptions.None))
                .Select(encodedLine => "/// " + encodedLine);

            string docCommentXml = $"/// <summary />{Environment.NewLine}";
            if (encodedLines.Any())
            {
                docCommentXml = $"/// <summary>{Environment.NewLine}{string.Join(Environment.NewLine, encodedLines)}{Environment.NewLine}/// </summary>{Environment.NewLine}";

            }

            return SyntaxFactory.TriviaList(
                SyntaxFactory.ParseLeadingTrivia(
                    docCommentXml
                )
            );
        }
    }
}

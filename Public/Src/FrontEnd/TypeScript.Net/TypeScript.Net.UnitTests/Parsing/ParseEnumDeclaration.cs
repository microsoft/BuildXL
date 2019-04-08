// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using Xunit;

namespace TypeScript.Net.UnitTests.Parsing
{
    public class ParseEnumDeclaration
    {
        [Fact]
        public void SimpleDeclaration()
        {
            string code = @"
import * as X from 'blah';
namespace TopLevelNamespace {
    export enum Foo2 {
        Case1 = 1,
        Case2 = 3,
    }

   let x = 42;

   var y = Foo2.Case1;
   let z = x + <number>(y);

   interface Foo {
      x: number
   }

   let f: Foo = {x: 42};
   function foo(f: Foo) {return f.x;}

   let z2 = foo(f);
   z2 = foo({x: f.x})
   let ar = [z2, 42];
   ar = [...ar, 1];

   declare var process: any;
}";
            var node = ParseAndEnsureNodeIsNotNull(code);
            Console.WriteLine(node);
        }

        [Fact]
        public void ParseArtifactDs()
        {
            string code = @"
// TODO: remove String inheritance and optional lambdas once we have support for single quotes in the IDE
interface Path extends String  {
}
/** Source files can be denoted by their paths. */
type SourceFile = Path;

/** Derived files represent paths generated during a build. */
interface DerivedFile {
    __derivedFileBrand: any;
}

/** Files are either source or derived files. */
type File = SourceFile | DerivedFile;
";
            ParseAndEnsureNodeIsNotNull(code);
        }

        [Fact]
        public void ParseCustomFormatFunction()
        {
            string code = @"
interface Path extends String  {
}
declare function _(format: string, ...args: any[]): Path;

let x: Path = _`foo`;
";

            ParseAndEnsureNodeIsNotNull(code);
        }

        [Fact]
        public void ParseArtifactDotDsc()
        {
            string code = @"
namespace Artifact {
    /**Argument type for inputs function. This is needed only due to current restrictions of the interpreter. */
    type InputsArg = Path | File | Directory | StaticDirectory;
    
    /**
     * Factory method that lifts path, file or folder to input artifact.
     */
    export function input(value: File): Artifact {
        return createArtifact(value, ArtifactKind.input);
    }
    
    /**
     * Factory method that lifts path to rewritten artifact.
     */
    export function rewritten(originalInput: Path, outputPath: Path): Artifact {
        return createArtifact(outputPath, ArtifactKind.rewritten, originalInput);
    }
    
    /**
     * Factory method that lifts list of paths, files or folders to array of input artifacts.
     */
    export function inputs(values: InputsArg[]): Artifact[] {
        return (values || []).map(input);
    }
    
    /**
     * Factory method that lifts path, file of folder to output artifact.
     */
    export function output(value: Path): Artifact {
        return createArtifact(value, ArtifactKind.output);        
    }
    
    /**
     * Factory method that lifts path, file or folder to input directory artifact.
     */
    export function outputFolder(value: Path): Artifact {
        return createArtifact(value, ArtifactKind.outputFolder);
    }
        
    /**
     * Factory method that lifts list of paths, files or folders to array of output artifacts.
     */
    export function outputs(values: Path[]): Artifact[] {
        return (values || []).map(output);
    }
    
    function createArtifact(value: File, kind: ArtifactKind, original?: File): Artifact {
        if (value === undefined) {
            return undefined;
        }
        
        return <Artifact>{
            path: value,
            kind: kind,
            original: original
        };
    }
}";

            ParseAndEnsureNodeIsNotNull(code);
        }

        private INode ParseAndEnsureNodeIsNotNull(string code)
        {
            var parser = new Parser();
            INode node = parser.ParseSourceFile(
                "fakeFileName.ts",
                code,
                ScriptTarget.Es2015,
                syntaxCursor: null,
                setParentNodes: false,
                parsingOptions: ParsingOptions.DefaultParsingOptions);

            Assert.NotNull(node);
            return node;
        }
    }
}

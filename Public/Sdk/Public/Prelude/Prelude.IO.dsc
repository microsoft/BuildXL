// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <reference path="Prelude.Core.dsc"/>

/** 
 * Glob to get a list of files; if pattern is undefined, then the default is "*". 
 * Returns the empty array if the globed directory does not exist.
 * 
 * Glob supports one special case: if the pattern starts with *\ or the unix version with /
 * For example if you run: glob(d`.`, "*\a.txt") with the following directory structure:
 * 
 * \
 * │   a.txt
 * ├───F1
 * │       a.txt
 * │
 * ├───F2
 * │       b.txt
 * │
 * └───F3
 *     │   a.txt
 *     │   b.txt
 *     │
 *     └───F4
 *             a.txt
 * 
 * it will return:
 *  * f1/a.txt,
 *  * f3/a.txt
 * 
 * It will not match any files in the passed folder. i.e. it will not match /a.txt in the root
 * It will also not recurse after one level of folders, i.e. it will not match /f3/f4/a.txt
 */
declare function glob(folder: Directory, pattern?: string): File[];

/** 
 * Glob to get a list of Directories; if pattern is undefined, then the default is "*". if isRecursive is not defined, 
 * the default is false.
 * Returns the empty array if the globed directory does not exist. 
 */
declare function globFolders(folder: Directory, pattern?: string, isRecursive?: Boolean): Directory[];

/** 
 * Glob files recursively; if pattern is undefined, then the default is "*" 
 * Returns the empty array if the globed directory does not exist.
 */
declare function globR(folder: Directory, pattern?: string): File[];
declare function globRecursively(folder: Directory, pattern?: string): File[];

/**
 * Import require
 * This function returns undefined if the first argument cannot be resolved.
 * TODO: The semantics may change.
 */
declare function importFrom(path: File | string, qualifier?: any): any;

/**
 * Imports a given file.
 * Used in configuration files only.
 */
declare function importFile(path: File): any;


/** 
 * This method is obsolete and will be removed soon.
 * Use importFrom instead.
 */
declare function require(path: File | string, qualifier?: any): any;

/**
 * Represents path atoms: path segments and extensions.
 */
interface PathAtom {
    __brandPath: any;
    
    /** Turns this path atom into a relative path. */
    asRelativePath: () => RelativePath;

    /**
     * Changes the extension of a path atom and fails with an error if the extension is not a valid PathAtom.
     * Use the empty string as an argument for removing the extension.
     * If the path atom doesn't have an extension, the specified extension is appended.
     */
    changeExtension: (extension: PathAtomOrString) => PathAtom;

    /** Concats path atoms; fails with an error if the provided path atom is invalid. */
    concat: (atom: PathAtomOrString) => PathAtom;

    /**
     * Checks if this path atom equals the argument. If ignoreCase argument is absent,
     * then the check will be based on file name comparison rules of the current operating system.
     */
    equals: (atom: PathAtomOrString, ignoreCase?: boolean) => boolean;

    /** Gets the extension; returns undefined when the extension is missing. */
    getExtension: () => PathAtom;
    
    /** Gets the extension; returns undefined when the extension is missing. */
    extension: PathAtom;

    /** Checks if this path atom has an extension. */
    hasExtension: () => boolean;

    /* Returns a string representation of this path atom */
    toString: () => string;
}

/**
 * This is a temporary workaround till we have a specific built-in syntax to build a PathAtom literal. E.g. '@literal' or |literal|
 */
namespace PathAtom {
    /** Creates a path atom from a string. */
    export declare function create(value: string): PathAtom;

    /** Creates a path atom by interpolation. */
    export declare function interpolate(value: PathAtomOrString, ...rest: PathAtomOrString[]);
}

/**
 * Atom can be a string or a path atom.
 * This type is a shorthand for writing signatures of methods of path atoms and absolute paths.
 * TODO: this might be the indicator of the need of implicit conversion (string -> PathAtom) or function overloading
 */
type PathAtomOrString = PathAtom | string;

/**
 * Represents relative paths.
 */
interface RelativePath {
    /**
     * Changes the extension of this relative path and fails with an error if the extension is not a valid PathAtom.
     * Use the empty string as an argument for removing the extension.
     * If the path atom doesn't have an extension, the specified extension is appended.
     */
    changeExtension: (extension: PathAtomOrString) => RelativePath;

    /**
     * Extends this relative path with new path components.
     * Example:
     *     RelativePath.create("a/b/c").combine("d/e/f") == RelativePath.create("a/b/c/d/e/f").
     */
    combine: (fragment: PathFragment) => RelativePath;

    /**
     * Extends this relative path with a sequence of new path components.
     * This method provides a convenient way to combine relative paths.
     * Example:
     *     let fragments = ["d/e/f", "g/h/i"];
     *     RelativePath.create("a/b/c").combinePaths(...fragments) == RelativePath.create("a/b/c/d/e/f/g/h/i").
     */
    combinePaths: (...fragments: PathFragment[]) => RelativePath;

    /**
     * Concatenates a path atom to the end of this relative path.
     * Example:
     *     RelativePath.create("a/b/c").concat("xyz") == RelativePath.create("a/b/cxyz").
     */
    concat: (atom: PathAtomOrString) => RelativePath;

    /**
     * Checks if this relative path equals the argument. If ignoreCase argument is absent,
     * then the check will be based on file name comparison rules of the current operating system.
     */
    equals: (fragment: RelativePath | string, ignoreCase?: boolean) => boolean;

    /** Gets the extension; returns undefined when the extension is missing. */
    getExtension: () => PathAtom;
    
    /** Gets the extension; returns undefined when the extension is missing. */
    extension: PathAtom;

    /**
     * Gets the parent of this relative path, i.e., removes the last segment of the relative path.
     * Returns undefined if this relative path has no parent.
     */
    parent: RelativePath;

    /** Gets the last component of this relative path, and returns undefined if this relative path is empty. */
    name: PathAtom;

    /** Gets the last component of this relative path without extension, and returns undefined if this relative path is empty. */
    getNameWithoutExtension: () => PathAtom;

    /** Gets the last component of this relative path without extension, and returns undefined if this relative path is empty. */
    nameWithoutExtension: PathAtom;

    /** Checks if this relative path has an extension. */
    hasExtension: () => boolean;

    /** Checks if this relative path has a parent. */
    hasParent: () => boolean;

    /** Converts to an array of path atoms. */
    toPathAtoms: () => PathAtom[];

    /* Returns a string representation of this relative path. */
    toString: () => string;
}

namespace RelativePath {
    /**
     * Creates a relative path from a string.
     * Example:
     *     let x = RelativePath.create("a/b/c");
     */
    export declare function create(value: string): RelativePath;

    /**
     * Creates a relative path from an array of path atoms.
     * Example:
     *     RelativePath.fromPathAtoms(PathAtom.create("a"), "b", "c") == RelativePath.create("a/b/c").
     */
    export declare function fromPathAtoms(...atoms: PathAtomOrString[]): RelativePath;

    /** Creates a relative path by interpolation. */
    export declare function interpolate(value: PathFragment, ...rest: PathFragment[]);
}

type PathFragment = PathAtom | RelativePath | string;

interface PathQueries {
    /** 
     * Gets the path extension; returns undefined for invalid extension. 
     */
    extension: PathAtom;

    /** 
     * Checks if this path has an extension. 
     */
    hasExtension: () => boolean;

    /**
     * Gets the parent of this path, i.e., removes the last segment of the path.
     * Returns undefined if the path has no parent.
     */
    parent: Path;

    /** 
     * Checks if this path has a parent. 
     */
    hasParent: () => boolean;

    /** 
     * Gets the last component of this path. 
     */
    name: PathAtom;

    /**
     * Gets the last component of this path without extension.
     * This method is equal to .name.changeExtension("").
     */
    nameWithoutExtension: PathAtom;

    /** 
     * Checks if this path is within the specified container. 
     */
    isWithin: (container: Path | Directory | StaticDirectory) => boolean;

    /**
     * Gets the relative path of this path with respect to the argument.
     * Returns undefined if the provided Path is not a descendant path of this path.
     * */
    getRelative: (relativeTo: Path) => RelativePath;

    /**
     * Returns this path.
     * This method is useful so that paths can be used to denote source files.
     */
    path: Path;

    /**
     * Gets the root of the path. On windows file systems this will be the drive letter for example: 'c:', on unix filesystems this will be '/'
     */
    pathRoot: Path;
    
    /** 
     * Helper method to get a file or path printed as a string for diagnostic purposes.
     * The result will be OS specific.
     *
     * !!! WARNING !!!
     * The result of this function should NEVER be used on a command-line, response file or file written to disk.
     * This string should only be used for diagnostic purposes.
     * If this string is used in a Pip, BuildXL loses the ability to:
     *  - Canonicalize the paths cross platform,
     *  - Normalize the paths for shared cache. i.e. c:\windows vs d:\windows on other machines, or folders with the current user
     *  - Compress the paths in all storage forms
     * Doing this risks breaking caching, distribution and shared cache.
     */
    toDiagnosticString: () => string;
}

interface PathCombineLike {
    /**
     * Extends this path with the specified path fragment.
     * The string can represent either a path atom (e.g. "csc.exe") or a relative path (e.g. "a/b/csc.exe").
     * Abandons if the provided atom can't be a valid relative path.
     */
    combine: (fragment: PathFragment) => Path;

    /**
     * Extends this path with the specified list of path fragments.
     * Each string can represent either a path atom (e.g. "csc.exe") or a relative path (e.g. "a/b/csc.exe").
     * Abandons if any of the provided atoms can't be a valid path atom.
     * This method provides a convenient way to combine path fragments.
     * Example:
     *     let fragments = ["a/b", "c/d", "e/f"];
     *     let outDir = 'bin/debug';
     *     let fullOutPath = outDir.combinePaths(...fragments);
     * Of course you can also do it as
     *     let fullOutPath = outDir.combine(fragments.join("/"));
     * but if fragments is an array of path atoms or relative paths, then array join cannot be used.
     */
    combinePaths: (...fragments: PathFragment[]) => Path;
}

/**
 * Represents an absolute path.
 *
 * To create a Path from a string, use call the 'p' function:
 *
 *   p`my_path_as_string`
 */
interface Path extends PathQueries, PathCombineLike {
    __brandPath: any;

    /**
     * Changes extension of a path and abandons if the extension is not a valid PathAtom.
     * Use the empty string as an argument for removing the extension.
     */
    changeExtension: (extension: PathAtomOrString) => Path;

    /**
     * Relocates this path from its container (source container) to a new target container, possibly giving a new extension.
     * Abandons if the new extension is provided and it can't be a valid path atom.
     * Error if the path is not within the specified container.
     * */
    relocate: (sourceContainer: Directory | StaticDirectory, targetContainer: Directory | StaticDirectory, newExt?: PathAtomOrString) => Path;

    /**
     * Extends path by a string or path atom at the, e.g., 'a/b/Foo'.concat("Bar") === 'a/b/FooBar'.
     */
    concat: (atom: PathAtomOrString) => Path;

    /**
     * Extends path by a string or path atom at the, e.g., 'a/b/Foo'.extend("Bar") === 'a/b/FooBar'.
     * This method is obsolete, use concat.
     */
    @@obsolete('Use "concat" method instead.')
    extend: (atom: PathAtomOrString) => Path;

    /**
     * Returns a string representation of this path.
     * The string representation has an invalid path character. This character is a mechanism
     * to prevents users (although not bullet-proof) from using paths and strings interchangeably.
     */
    toString: () => string;
}

/**
 * Represents a source directory artifact.
 *
 * To create a Directory from a string, call the 'd' function, i.e.:
 *
 *   d`my_path_as_string`
 */
interface Directory extends PathQueries, PathCombineLike {
    __directoryBrand: any;

    /**
     * Returns a string representation of this directory.
     * The string representation has an invalid path character. This character is a mechanism
     * to prevents users (although not bullet-proof) from using paths and strings interchangeably.
     */
    toString: () => string;
}

/** Files are either source or ss. */
interface File extends PathQueries {
    __fileBrand: any;
    
    /**
     * Returns a string representation of this derived file.
     * The string representation has an invalid path character. This character is a mechanism
     * to prevents users (although not bullet-proof) from using paths and strings interchangeably.
     * Moreover, the string representation includes the rewrite count of this file.
     */
    toString: () => string;
}

/** Derived files represent artifacts generated during a build. */
interface DerivedFile extends File {
    __derivedFileBrand: any;
}

/**
 * SourceFile represents a file that is not an artifact generated during a build.
 *
 * To create a SourceFile from a string, call the 'f' function, i.e.:
 *
 *   f`my_path_as_string`
 */
interface SourceFile extends File {
    __sourceFileBrand: any;
}

/** All the available flavors of static directories */
type StaticDirectoryKind =  "shared" | "exclusive" | "sourceAllDirectories" | "sourceTopDirectories" | "full" | "partial";

/**
 * Represents a sealed Directory whose content (statically-known or not) is 'frozen' in time.
 * TODO: This interface should be renamed to something like 'SealedDirectory' and the set of 
 * methods that interact with the content (getFiles, etc.) moved to StaticContentDirectory
 */
interface StaticDirectory extends PathQueries {
    __staticDirectoryBrand: any;

    /** The kind of a static directory */
    kind: StaticDirectoryKind;

    /** Gets the root directory. */
    root: Directory;

    /** Gets the sealed directory. */
    /** This method will be deprecated in favour of root property. */
    getSealedDirectory: () => Directory;

    /** Gets the file given its path; returns undefined if no such a file exists in this static directory. */
    getFile: (path: Path | PathFragment) => File;
    
        /** Gets an array of files given its paths; returns undefined in the result array if no such a file exists in this static directory. */
    getFiles: (path: (Path | PathFragment)[]) => File[];

    /** Checks whether the sealed directory has the given file. */
    hasFile: (path: Path | PathFragment) => boolean;
    
        /** Gets the contents of this static directory. */
    contents: File[];

    /** Gets the content of this static directory. */
    /** This method will be deprecated in favour of contents property. */
    getContent: () => File[];

    /**
     * Returns a string representation of this static directory.
     * The string representation has an invalid path character. This character is a mechanism
     * to prevents users (although not bullet-proof) from using paths and strings interchangeably.
     */
    toString: () => string;
}

/** A shared opaque directory. Its static content is always empty. */
interface SharedOpaqueDirectory extends StaticDirectory {
    kind: "shared"
}

/** An exclusive opaque directory. Its static content is always empty. */
interface ExclusiveOpaqueDirectory extends StaticDirectory {
    kind: "exclusive"
}

/** A source sealed directory where only the top directory is sealed. Its static content is always empty. */
interface SourceTopDirectory extends StaticDirectory {
    kind: "sourceTopDirectories"
}

/** A source sealed directory where all directories, recursively, are sealed. Its static content is always empty. */
interface SourceAllDirectory extends StaticDirectory {
    kind: "sourceAllDirectories"
}

/** A fully sealed directory. Its content is known statically and can be retrieved. */
interface FullStaticContentDirectory extends StaticDirectory {
    kind: "full"
}

/** A partial sealed directory. Its content is known statically and can be retrieved. */
interface PartialStaticContentDirectory extends StaticDirectory {
    kind: "partial"
}

/**
 * Directory-related types that group semantically related directories, mainly for convenience
 */
type StaticContentDirectory = FullStaticContentDirectory | PartialStaticContentDirectory;
type SourceDirectory = SourceTopDirectory | SourceAllDirectory;
type OpaqueDirectory = SharedOpaqueDirectory | ExclusiveOpaqueDirectory;

// -----------------------------------------------------------
//  Factory functions for creating path-related artifacts.
// -----------------------------------------------------------

/** Text encoding. */
const enum TextEncoding {
    ascii = 0,
    bigEndianUnicode,
    unicode,
    utf32,
    utf7,
    utf8
}

namespace File {
    /** Returns a file artifact located at a given path (no actual file is created on disk!) */
    export declare function fromPath(p: Path): File;
    
    /** 
     * Read a source file as text.
     * The source file will be tracked by the input tracker of BuildXL's engine, in the same way as spec files.
     * Note that since it takes a source file as an input, this function cannot be used to read a derived file,
     * or a file produced by a pip. 
     */
    export declare function readAllText(file: SourceFile, encoding?: TextEncoding): string;

    /** Returns true if a given file exists on disk. */
    export declare function exists(file: SourceFile): boolean;
}

namespace Directory {
    /** Returns a directory artifact located at a given path (no actual directory is created on disk!) */
    export declare function fromPath(p: Path): Directory;

    /** Returns true if a given directory exists on disk. */
    export declare function exists(path: Directory): boolean;
}

namespace Path {
    export declare function interpolate(root: Path | Directory | StaticDirectory, ...rest: PathFragment[]): Path;
}

/** Creates an absolute path from a string. */
declare function p(format: string, ...args: any[]): Path;

/** Creates a directory artifact (doesn't create a physical directory in the file system). */
declare function d(format: string, ...args: any[]): Directory;

/** Creates a file artifact (doesn't create a physical file in the file system). */
declare function f(format: string, ...args: any[]): SourceFile;

/** Creates a relative path from a string. */
declare function r(format: string, ...args: any[]): RelativePath;

/** Creates a path atom from a string. */
declare function a(format: string, ...args: any[]): PathAtom;

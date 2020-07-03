Comments are often used to add hints, notes, suggestions, or warnings to DScript code. They serve to make the code easier to read and understand.

DScript supports three types of comments:
# Single line comments
Single-line comments start with two slashes (`//`) and marks all text following it on the same line as a comment.

```ts
// This is a valid single line comment
const x = 10; // This too
const y = 10 // This line will fail as the following semicolon is part of the comment, not the code ;
```

# Multi-line comments
Comments can span multiple lines by using the multiple-line comment style. These comments start with `/*` and end with `*/`. The text between those markers is the comment.

```ts
/* This is 
    a multiline comment */
const x = 10; /* They can be anywhere */
const x = /* even in the middle of an expression */ 10;
```

# Doc Comments

Comments having a special form can be used to direct various tool to get more information from the source code elements. Such comments are a special form of multi-line comments that start with a slash and two asterisks (`/**`). They must immediately precede a user-defined type (such as an interface, union type, enum or type alias), a member (such as a property, enum member or function) that they annotate or a variable declaration.

Documentation comments support various tags (using `@tagName` syntax) to add more meaning to the code.

```ts
/**
  * Sample function that adds two numbers.
  * @param {left} - The left argument to be added 
  * @param {right} - The left argument to be added 
  * @returns {number} Sum of a and b
  */
function add(left: number, right: number) : number { 
    return left + right;
};
```

The following tags provide commonly used functionality in a user documentation (for the full list of tags see [JsDoc standard](http://userjsdoc.org/)).

| __Tag__          |  __Purpose__                                           |
|------------------|--------------------------------------------------------|
| [`@author`](http://usejsdoc.org/tags-author.html)            |  Identify the author of an item.                           | 
| [`@copyright`](http://usejsdoc.org/tags-copyright.html)        |  Document some copyright information.             |
| [`@description`](http://usejsdoc.org/tags-description.html)     |  Describe a symbol.                                    |
| [`@example`](http://usejsdoc.org/tags-example.html)   |  Provide an example of how to use a documented item.           |
| [`@license`](http://usejsdoc.org/tags-license.html)      | Identify the license that applies to this code.                     |
| [`@name`](http://usejsdoc.org/tags-name.html)         | Document the name of an object.                                 |
| [`@param`](http://usejsdoc.org/tags-param.html)         | Document the parameter to a function.                   |
| [`@private`](http://usejsdoc.org/tags-private.html)        | This symbol is meant to be private.       |
| [`@returns`](http://usejsdoc.org/tags-returns.html)     | Document the return value of a function               |
| [`@see`](http://usejsdoc.org/tags-see.html)   | Refer to some other documentation for more information.        |
| [`@summary`](http://usejsdoc.org/tags-summary.html)       | A shorter version of the full description.           |


Documentation comments can show up in the IDE's IntelliSense and used by the DSDoc tool.
# Functions

DScript specifications can utilize functions which contain the logic of what should happen. A typical function looks like:

## Example
```ts
function myFunction(arg: Arguments): Result {
    // Method statements go here
};
```

In this example the name of the function is `myFunction`.  It has a single argument named `arg` which is of type `Arguments`. 

## Arguments.
Functions can take multiple arguments separated by a comma.
Arguments can be declared to be optional by placing a question mark (`?`) after the name of the argument.

```ts
function myHelper(requiredArg: string, optionalArg?: string): string {
   return "result";
};
```
While functions can take multiple arguments, for primary Sdk functions like tool wrappers or functions that represent building something complex like a static library, managed executable etc. It is highly recommended to take a single argument that is an interface. This will allow the easiest composition and extension for others later.

## Return value
Since all logic in BuildXL is side-effect free, it doesn't make sense to not have a return value. Therefore methods that return `void` are not allowed in DScript as you might as well not even call the function.

## Guidelines
Functions have the same visibility rules as other declarations using `export` and `@@public`.
Function names are recommended to be [camelCase](https://en.wikipedia.org/wiki/Camel_case)


## Method statements
The following statement types are allowed inside methods:
* `const` and `let` declarations. If you don't reassign the variable you should use const.
   ```ts
    const x = 10;
    let y = [1];
   ```
* Assignment declaration of `let` variables:
   ```ts
   y = y.push(2);
   ```
* `if` statement:
   ```ts
   if (x === 10) {
       // more statements
   }
   ```
* `for of` enumerations:
   ```ts
   for (let item of y) {
       // more statements
   }
   ```
* `return` statement
   ```ts
   return 10;
   ```
* `switch` statements
   ```ts
   switch (x) {
       case 10: 
           // more statements
           break;
       case 20: 
           // more statements
           break;
       default: 
           // more statements
           break;
   }
   ```
* block statements
   ```ts
   {
        // more statements
   }
   ```
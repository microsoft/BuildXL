# Merging and overriding objects

## Introduction
When using and apply templates one has to combine objects to one another.
This document describes two approaches, `merge` and `override`. Where the first is a deep merge application and the second a shallow. As stated in the template document, we want to value that defies the final settings (not the defaults) to have fine grained control on how the merging happens. I.e. for lists if the items should replace all the defaults, be prepended or appended.

## Merge vs Override
In certain cases you want the template application to be deep or shallow. BuildXL therefore defines two functions on every object: `merge` and `override`.

`override` is the shallow application and `merge` is the deep recursive one.

```typescript
interface Object {
    override<T>(other: Object): T;
    merge<T>(other: Object): T;
}
```

Logically `override` means whatever is on the right hand side will be in the result. `merge` will recursively merge for objects as well as for arrays, sets and maps.

Before we dive into more details, some concrete examples would be:

```typescript
// When all fields are strings merge and override have the same behavior.
{a: "al", b: "bl"}.override({b: "br", c: "cr"}) === {a: "al", b: "br", c: "cr"}
{a: "al", b: "bl"}.merge(   {b: "br", c: "cr"}) === {a: "al", b: "br", c: "cr"}

// For nested objects the result of merge and override differ.
{obj: {a: "al", b: "bl"} }.override({obj: {b: "br", c: "cr"}} ) === {obj: {         b: "br", c: "cr"}}
{obj: {a: "al", b: "bl"} }.merge(   {obj: {b: "br", c: "cr"}} ) === {obj: {a: "al", b: "br", c: "cr"}}

// As well as for arrays.
{a: ["al"], b: ["bl"]}.override({b: ["br"], c: ["cr"]} ) === {a: ["al"], b: [      "br"], c: ["cr"]}
{a: ["al"], b: ["bl"]}.merge(   {b: ["br"], c: ["cr"]} ) === {a: ["al"], b: ["bl", "br"], c: ["cr"]}
```

## Customizing the merge
Not all merges are generically applicable.
The most common case would be lists. Especially for native code the order of [/DKey=Value](https://msdn.microsoft.com/en-us/library/hhzbb5c8.aspx) on the command line of the c compiler is order specific. So sometimes the project needs to insert a define at the front of the list, sometimes at the end of the list and sometimes the would like to replace the entire list.
We could create some special sauce for arrays, lists and maps but it is nicer to create a generic mechanism to allow this that can be used by any object. For instance a value that has a field defined which is a flags-enum might prefer a bit-wise or operator.
The generic way is that we inspect the arrays and objects that are merged to see if they define a custom merge function on the objects to be merged. The merge logic would then check if there was one present and use that one in favor of the default merge behavior.

In BuildXL the Prelude would be extended by defining extending following types.

```typescript
type CustomMergeFunction<T> = (left: T, right : T) => T;

interface Object {
    /** If set, the function to call when merging this object */
    customMerge?: CustomMergeFunction<Object>;

    /** A way to set the custom merge function. Similar to Array.add to handle BuildXL's immutability, this returns the new object.*/
    withCustomMerge<T>(customMergeFunction: CustomMergeFunction<T>) : T
};

interface Array<T> {
    /** If set, the function to call when merging this array */
    customMerge?: CustomMergeFunction<Array<T>>;

    /** A way to set the custom merge function in continuation style */
    /** A way to set the custom merge function. Similar to Array.add to handle BuildXL's immutability, this returns the new array.*/
    withCustomMerge(customMergeFunction: CustomMergeFunction<Array<T>>) : Array<T>;
};
```

These functions can then be used to perform common operations on lists. If so they would then look like:

```typescript
const listAB = ["A", "B"];
const listCD = ["C", "D"];

const appended  = listAB.merge( listCD.withCustomMerge((l, r) => l.concat(r)) ); // ["A", "B", "C", "D"]
const prepended = listAB.merge( listCD.withCustomMerge((l, r) => r.concat(l)) ); // ["C", "D", "C", "D"]
const replaced  = listAB.merge( listCD.withCustomMerge((l, r) => r) );           // ["C", "D"]
```

Of course these three list operations would be extremely common. Therefore we will support these three built-in via methods on all array's and lists. Therefore one can write:

```typescript
const listAB = ["A", "B"];
const listCD = ["C", "D"];

const appended  = listAB.merge( listCD.appendWhenMerged() );  // ["A", "B", "C", "D"]
const prepended = listAB.merge( listCD.prependWhenMerged() ); // ["C", "D", "C", "D"]
const replaced  = listAB.merge( listCD.replaceWhenMerged() ); // ["C", "D"]
```


A way to do bit-wise-or on a numeric field of an object would then look like:

```typescript
const left = {
    keyA: 2,
    keyB: "left",
    keyC: "left"
};
const right = {
    keyA: 4,
    keyB: "right",
    keyD: "right"
}

// This custom merge function can be written way more succinct, but is expanded here for clarity
const result = left.merge( right.withCustomMerge((l, r) => {
    // first do standard merge
    let merge = l.merge(r);
    //and then set the bit-wise
    let bitWiseKeyA = l.keyA | l.keyB;
    // return the merge result but override keyA with the bitwise value.
    return merge.override( { keyA: bitWiseKeyA } );
}));

// Result will be equal to:
const realResult = {
    keyA: 6,        // bit-wise combined using the customMerge from the left side.
    keyB: "right",
    keyC: "left",
    keyD: "right",
};
```


## Template guided custom merge functions.
In the examples in the previous section we have shown to use the `withCustomMerge` on the right hand side of the merge. This is in line with the project controlling the merge behavior. Although sometimes you might want to have the SDK drive this behavior, i.e., the custom merge is defined on the left side of values to be merged. A good example would have been the bitwise operator. That belongs inside the SDK function. Therefore when merge is applied, both the left and right value can define the `customMerge` field. When both sides define it, the right custom merge obviously wins.

```typescript
const sdkDefault : Arguments = {
    keyA: 2,
    keyB: "left",
    keyC: "left",

    // This is the same bit-wise-merge as the previous example exact more succinct.
    customMerge: (l, r) => l.merge(r).override( { keyA: l.keyA | l.keyB } )
});

function mySdk(args: Arguments) {
    return sdkDefault.merge(args);
};

const result = mySdk({
    keyA: 4,
    keyB: "right",
    keyD: "right"
});

// Result will be equal to:
const realResult = {
    keyA: 6,        // bit-wise combined using the customMerge from the left side.
    keyB: "right",
    keyC: "left",
    keyD: "right",
};
```

## Transitive carry of the custom merge function.
The question here is which operations carry the `customMerge` field. I.e., if I have an array with a `customMerge` function set, and I add an element that creates a new  array with the extra element. Does the resulting array have the `customMerge` field set as well, or is it lost?
In both cases (keep, or remove) one can make arguments for it's convenience and its potential for confusion.

## Removing fields and undefined.
When accessing members in DScript one cannot distinguish whether a field is set, or whether it is set but assigned to undefined.

```typescript
const isTrue_1 = {                    }.myField === undefined;
const isTrue_2 = { myField: undefined }.myField === undefined;
```

Because we want to allow users to remove values when applying templates, one can override objects by setting the fields to undefined. For example:

<div class="warning" style="color:red">Implementation has to be updated since it currently leaves keyA as is.</div>

```typescript
const result = {keyA: "left"}.merge({keyA: undefined});
// This results in an object with field keyA set to undefined.
```

This potentially breaks some abstraction since now it is observable if an object has a field set to undefined or is not present by using merge via the following logic.
> Note: There is currently another way in that each object exposes a field `keys` of type `string[]`

```typescript
function hasKeyA(objectUnderTest: object) : bool {
    let const value = "totallyUniqueValueForA";

    return {keyA: value}.merge(objectUnderTest).keyA !== value;
}
```

One can argue this is a problem, but I believe in practice we don't need to do anything special here.

## Reference implementation

### Override
The logical implementation in Typescript for override would be:

```typescript
// All objects have an instance member called 'override'
interface Object {
    override<T>(other: Object): T;
}

// This is not how it is implemented, but conceptually you can see the method implemented by setting it on the prototype of object.
Object.prototype.override = function(right: Object){
    return override(this, right);
}

function override<T>(left: any, right: T) : T {
    // If both arguments are objects, merge objects
    if (typeof left === "object" && typeof right === "object") {
        // BuildXL is immutable, so we 'clone' the left object
        var result = left.clone();

        // Copy all keys from the right object to the left object.
        for (let key in right) {
            result[key] = right[key];
        }

        return <T>result;
    }

    // if right is undefined, then return left
    if (right === undefined) {
        return left;
    }

    // For all other types or mismatched types, the right value wins.
    return right;
}
```

### Merge
The logical implementation in Typescript for merge would be:

```typescript
// All objects have an instance member called 'merge'
interface Object {
    merge<T>(other: Object): T;

    /** If set, the function to call when merging this object */
    customMerge?: CustomMergeFunction<Object>;

    /** A way to set the custom merge function in continuation style */
    withCustomMerge<T>(customMergeFunction: CustomMergeFunction<T>) : T
}

// This is not how it is implemented, but conceptually you can see the method implemented by setting it on the prototype of object.
Object.prototype.merge = function(right: Object){
    return merge(this, right);
}

function merge<T>(left: any, right: T) : T {

    // right custom merge wins over left custom merge
    if (right.customMerge) {
        // Call the custom merge function, but remove the one from right to avoid infinite recursion when called.
        return right.customMerge(left, right.override({customMerge: undefined}));
    }

    // left custom merge applied if specified and right custom mere is not.
    if (left.customMerge) {
        // Call the custom merge function, but remove the one from left to avoid infinite recursion when called.
        return left.customMerge(left.override({customMerge: undefined}), right);
    }

    // If both arguments are arrays, merge as arrays
    if (left instanceof Array && right instanceof Array) {
        // Default operation for arrays is concat.
        return left.concat(right);
    }
    // Note that Sets and Maps also follow the array merge style but omitted here for brevity, they follow array and object respectively.

    // If both arguments are objects, merge objects
    if (typeof left === "object" && typeof right === "object") {
        return mergeObject(<Object>left, right);
    }

    // For all other types or mismatched types, the right value wins.
    return right;
}

function mergeObject<T>(left:Object, right:T) : T {
    // BuildXL is immutable, so we 'clone' the left object
    var result = left.clone();

    for (let key in right) {
        if (left.hasOwnProperty(key)) {
            // If the two objects have the same member, recurse the merge on them.
            result[key] = merge(left[key], right[key]);
        }
        else{
            // else the right value wins.
            result[key] = right[key];
        }
    }

    return <T>result;
}

// Extend all arrays (and also Set and Map) with mergeOperation helpers
interface Array<T> {
    /** If set, the function to call when merging this array */
    customMerge?: CustomMergeFunction<Array<T>>;

    /** A way to set the custom merge function in continuation style */
    withCustomMerge: (customMergeFunction: CustomMergeFunction<Array<T>>) => Array<T>;

    // Helper method to return an array object whose mergeOperation is set to the requested setting.
    prependWhenMerged: () => Array<T>;
    appendWhenMerged: () => Array<T>;
    replaceWhenMerged: () => Array<T>;
}

Array.prototype.appendWhenMerged: () => Array<T> = () => withCustomMerge((l,r) => l.concat(r));
Array.prototype.prependWhenMerged: () => Array<T> = () => withCustomMerge((l,r) => r.concat(l));
Array.prototype.replaceWhenMerged: () => Array<T> = () => withCustomMerge((l,r) => r || l);
```

## Nesting of argument structures
A common pattern for builders is to expose the details of the nested tools such that they can individually be controlled should the need arise.


## Templating, Functions and closure bindings
One has to realize that when one overrides a function in a template, as with the Sdk composition places. One had to realize that the functions are strictly bound. I.e. the overridden functions bind locally to the scope of the definition, they don't bind to the scope of the function they are replacing and the other functions don't get rebound:

```typescript
//SDK:
namespace Sdk {
    function library(args) {
        return "oldLib";
    }

    function executable(args) {
        return "oldExe";
    }

    function both(args) {
        return library(args) + "-" + executable(args);
    }
}

namespace Product {
    // Now, if the client will override library function somewhere in the hierarchy
    export const Sdk = $.Sdk.override({
            library: args => "newLib";
        });

    const t1 = Sdk.library(args);    // returns "newLib"
    const t2 = Sdk.executable(args); // returns "oldExe", new Sdk, but that one didn't override executable
    const t3 = Sdk.both(args);       // returns "oldLib-oldExe", new sdk, but both was not overridden old function stays bound to old library

    const t4 = $.Sdk.library(args);    // returns "oldLib", binds to the global Sdk so no overrides
    const t5 = $.Sdk.executable(args); // returns "oldExe", binds to the global Sdk so no overrides executable
    const t6 = $.Sdk.both(args);       // returns "oldLib-oldExe", binds to the global Sdk so no overrides executable
}
```


## Related technologies:
* [Jquery $.merge](http://api.jquery.com/jQuery.merge/) for arrays
  * A static method, not instance
  * Arrays append by default when merged, no customization on prepend/replace
* [Jquery $.extend](http://api.jquery.com/jQuery.extend/) for objects
  * A static method, not instance
  * default is shallow, but has a first argument to do a deep extend.
  * mutates existing object, but has pattern with passing empty argument to not mutate.
* [Javascript Object.assign](https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Object/assign)
  * Instance method on all objects


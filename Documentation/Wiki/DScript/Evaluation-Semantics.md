# Evaluation semantics: deferred evaluation

Being a data-flow language, DScript has a slightly different evaluation semantics from Typscript/JavaScript.
In DScript, global declarations are evaluated once and are evaluated in parallel with blocking semantics,
while statements in functions are evaluated top-down as usual.

Consider the following example:
```typescript
function func(caller: string, input: number) : number
{
    Debug.writeLine(`${caller}: ${input}`);
    return input + 1;
}

const x = func("x", 0);
const y = func("y", x);
const z = func("z", x);
```
This will output:
```console
x: 0
y: 1
z: 1
```
All right-hand side (rhs) expressions of `x`, `y`, and `z` are evaluated in parallel. Although `x` is referred twice, its rhs expression is only evaluated once. The evaluations of `y`'s and `z`'s rhs block until the evaluation result of
`x` is available, and thus the output `x: 0` always appears first. Because there is no data-flow dependency between `y` and `z`, the outputs `y: 1` and `z: 1` can appear in different orders. The end values after the evaluation are `x = 1`, `y = 2`, and `z = 2`. 



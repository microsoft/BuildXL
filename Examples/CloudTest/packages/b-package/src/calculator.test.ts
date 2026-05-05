import assert from "assert";
import { square, sumOfSquares } from "./calculator";

assert.strictEqual(square(5), 25, "square(5) should be 25");
assert.strictEqual(square(0), 0, "square(0) should be 0");
assert.strictEqual(sumOfSquares(3, 4), 25, "sumOfSquares(3,4) should be 25");

console.log("b-package: all 3 tests passed");

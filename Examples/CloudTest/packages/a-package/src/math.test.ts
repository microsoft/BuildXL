import assert from "assert";
import { add, multiply } from "./math";

assert.strictEqual(add(2, 3), 5, "add(2,3) should be 5");
assert.strictEqual(add(-1, 1), 0, "add(-1,1) should be 0");
assert.strictEqual(multiply(3, 4), 12, "multiply(3,4) should be 12");
assert.strictEqual(multiply(0, 100), 0, "multiply(0,100) should be 0");

console.log("a-package: all 4 tests passed");

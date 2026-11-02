import { add, multiply } from "@test/a-package";

export function square(x: number): number {
    return multiply(x, x);
}

export function sumOfSquares(a: number, b: number): number {
    return add(square(a), square(b));
}

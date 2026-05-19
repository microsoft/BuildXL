import { add, multiply } from "@test/a-package";

export function formatSum(a: number, b: number): string {
    return `${a} + ${b} = ${add(a, b)}`;
}

export function formatProduct(a: number, b: number): string {
    return `${a} * ${b} = ${multiply(a, b)}`;
}

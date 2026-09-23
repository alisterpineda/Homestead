import { test } from '@playwright/test';

type AsyncMethod<This, Args extends unknown[], Return> = (this: This, ...args: Args) => Promise<Return>;

/**
 * Reports a page-object method as one boxed `test.step`, so the HTML report and trace read as
 * "AccountListPage.submitCreate" rather than a dozen clicks and fills, and a failure inside points
 * at the calling line in the spec. String and number arguments ride along as step params.
 */
export function step(title?: string) {
  return function <This extends object, Args extends unknown[], Return>(
    target: AsyncMethod<This, Args, Return>,
    context: ClassMethodDecoratorContext<This, AsyncMethod<This, Args, Return>>,
  ): AsyncMethod<This, Args, Return> {
    return function (this: This, ...args: Args): Promise<Return> {
      const name = title ?? `${this.constructor.name}.${String(context.name)}`;
      const params = Object.fromEntries(
        args.flatMap((arg, i) => (typeof arg === 'string' || typeof arg === 'number' ? [[`arg${i}`, arg]] : [])),
      );
      return test.step(name, () => target.call(this, ...args), { box: true, params });
    };
  };
}

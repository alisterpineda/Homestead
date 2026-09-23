import { attemptSummary } from './processing-status.utils';

describe('attemptSummary', () => {
  it('says nothing for a job that has not opted into retries', () => {
    expect(attemptSummary({ status: 'Failed', attempts: 1, maxAttempts: 1 })).toBeNull();
    expect(attemptSummary({ status: 'Pending', attempts: 0, maxAttempts: 1 })).toBeNull();
  });

  it('numbers the next attempt of a job waiting to retry', () => {
    expect(attemptSummary({ status: 'Pending', attempts: 1, maxAttempts: 3 })).toBe('Retry 2/3');
  });

  it('counts the attempts a finally failed job used', () => {
    expect(attemptSummary({ status: 'Failed', attempts: 3, maxAttempts: 3 })).toBe('3/3 attempts');
  });

  it('notes a success that needed a retry, and only that', () => {
    expect(attemptSummary({ status: 'Completed', attempts: 1, maxAttempts: 3 })).toBe('Succeeded on attempt 2');
    expect(attemptSummary({ status: 'Completed', attempts: 0, maxAttempts: 3 })).toBeNull();
  });

  it('treats missing fields as a single-attempt job', () => {
    expect(attemptSummary({ status: 'Failed' })).toBeNull();
  });
});

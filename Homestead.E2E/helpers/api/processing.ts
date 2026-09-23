import { type APIRequestContext, expect } from '@playwright/test';

type JobStatus = 'Pending' | 'InProgress' | 'Completed' | 'Failed' | 'PartiallyCompleted' | 'Cancelled';

interface Job {
  id: string;
  status: JobStatus;
  entityId: string | null;
  error: string | null;
}

const ACTIVE: JobStatus[] = ['Pending', 'InProgress'];

async function listJobs(request: APIRequestContext, query = ''): Promise<Job[]> {
  const response = await request.get(`/api/v1/processing/jobs?Page=1&PageSize=200${query}`);
  await expect(response).toBeOK();
  return (await response.json()).items as Job[];
}

/** Waits for one job (newest-first listing) to finish; resolves to the finished job. */
export async function waitForJob(request: APIRequestContext, jobId: string, timeout = 60_000): Promise<Job> {
  let job: Job | undefined;
  await expect
    .poll(
      async () => {
        job = (await listJobs(request)).find((j) => j.id === jobId);
        return job?.status ?? 'NotListedYet';
      },
      { message: `job ${jobId} finishes`, timeout },
    )
    .toMatch(/^(Completed|Failed|PartiallyCompleted|Cancelled)$/);
  return job!;
}

/** Waits until no job about `entityId` (e.g. a document's text extraction) is pending or running. */
export async function waitForEntityJobs(request: APIRequestContext, entityId: string, timeout = 60_000): Promise<void> {
  await expect
    .poll(
      async () => (await listJobs(request, `&EntityId=${entityId}`)).filter((j) => ACTIVE.includes(j.status)).length,
      { message: `jobs for ${entityId} finish`, timeout },
    )
    .toBe(0);
}

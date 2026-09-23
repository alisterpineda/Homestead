import { type APIRequestContext, expect } from '@playwright/test';

export async function findUserIdByEmail(adminRequest: APIRequestContext, email: string): Promise<string> {
  const response = await adminRequest.get('/api/v1/admin/users?Page=1&PageSize=100');
  await expect(response, 'list users').toBeOK();
  const user = ((await response.json()).items as Array<{ id: string; email: string }>).find((u) => u.email === email);
  expect(user, `user ${email} exists`).toBeDefined();
  return user!.id;
}

export async function createGroupViaApi(adminRequest: APIRequestContext, name: string): Promise<{ id: string }> {
  const response = await adminRequest.post('/api/v1/admin/groups', { data: { name, description: null } });
  expect(response.status(), `create group ${name}`).toBe(201);
  return response.json();
}

export async function addGroupMemberViaApi(
  adminRequest: APIRequestContext,
  groupId: string,
  userId: string,
  accessLevel: 'Owner' | 'Editor' | 'Viewer',
): Promise<void> {
  const response = await adminRequest.post(`/api/v1/admin/groups/${groupId}/members`, { data: { userId, accessLevel } });
  expect(response.status(), 'add group member').toBe(201);
}

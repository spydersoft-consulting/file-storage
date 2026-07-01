import { test, expect } from '@playwright/test';
import { readOnlyToken } from '../playwright.config';

const BASE = process.env.FILESTORE_BASE_URL ?? 'http://localhost:5300';

test('FileStore_GET_NoToken_Returns401', async ({ playwright }) => {
  const anon = await playwright.request.newContext({ baseURL: BASE, extraHTTPHeaders: {} });
  try {
    const response = await anon.get('/api/v1/filestore');
    expect(response.status()).toBe(401);
  } finally {
    await anon.dispose();
  }
});

test('FileStore_POST_NoToken_Returns401', async ({ playwright }) => {
  const anon = await playwright.request.newContext({ baseURL: BASE, extraHTTPHeaders: {} });
  try {
    const response = await anon.post('/api/v1/filestore', {
      data: { source: 'x', entityType: 'x', entityId: 'x', fileName: 'x.pdf', contentType: 'application/pdf', sizeBytes: 1 },
    });
    expect(response.status()).toBe(401);
  } finally {
    await anon.dispose();
  }
});

test('Documents_GET_NoToken_Returns401', async ({ playwright }) => {
  const anon = await playwright.request.newContext({ baseURL: BASE, extraHTTPHeaders: {} });
  try {
    const response = await anon.get('/api/v1/documents');
    expect(response.status()).toBe(401);
  } finally {
    await anon.dispose();
  }
});

test('HealthCheck_NoToken_Returns200', async ({ playwright }) => {
  const anon = await playwright.request.newContext({ baseURL: BASE, extraHTTPHeaders: {} });
  try {
    const response = await anon.get('/livez');
    expect(response.status()).toBe(200);
  } finally {
    await anon.dispose();
  }
});

test('FileStore_POST_ReadOnlyToken_Returns403', async ({ playwright }) => {
  const readOnly = await playwright.request.newContext({
    baseURL: BASE,
    extraHTTPHeaders: { Authorization: `Bearer ${readOnlyToken}` },
  });
  try {
    const response = await readOnly.post('/api/v1/filestore', {
      data: { source: 'x', entityType: 'x', entityId: 'x', fileName: 'x.pdf', contentType: 'application/pdf', sizeBytes: 1 },
    });
    expect(response.status()).toBe(403);
  } finally {
    await readOnly.dispose();
  }
});

test('FileStore_DELETE_ReadOnlyToken_Returns403', async ({ playwright }) => {
  const readOnly = await playwright.request.newContext({
    baseURL: BASE,
    extraHTTPHeaders: { Authorization: `Bearer ${readOnlyToken}` },
  });
  try {
    const response = await readOnly.delete('/api/v1/filestore/00000000-0000-0000-0000-000000000001');
    // 403 from auth policy — 404 is acceptable only after auth passes; read-only token should get 403.
    expect(response.status()).toBe(403);
  } finally {
    await readOnly.dispose();
  }
});

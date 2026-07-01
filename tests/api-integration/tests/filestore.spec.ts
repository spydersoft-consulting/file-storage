import { test, expect, APIRequestContext } from '@playwright/test';
import type { FileDto, FileUrlResponse, InitiateUploadResponse } from './types';

const BASE = process.env.FILESTORE_BASE_URL ?? 'http://localhost:5300';
// Minimal valid PDF bytes — small enough not to slow tests, real enough for MinIO to accept.
const TINY_PDF = Buffer.from('%PDF-1.4 1 0 obj<</Type/Catalog>>endobj');

function uniqueSource(): string {
  return `pw-fs-${crypto.randomUUID().replaceAll('-', '').slice(0, 12)}`;
}

// The Aspire DCP proxy for MinIO exposes port 9000 over plain HTTP, but the S3 SDK
// may generate presigned URLs with https:// based on how the endpoint reference resolves.
// Also, Playwright's extraHTTPHeaders include Authorization: Bearer which MinIO rejects
// alongside query-string presigned auth. Use native fetch (no extra headers) for uploads.
function toLocalHttp(url: string): string {
  return url.replace(/^https:\/\/(localhost|127\.0\.0\.1):/, 'http://$1:');
}

async function s3Put(url: string, data: Buffer, contentType: string): Promise<void> {
  const response = await fetch(toLocalHttp(url), {
    method: 'PUT',
    headers: { 'Content-Type': contentType },
    body: data,
  });
  if (!response.ok) {
    throw new Error(`S3 presigned PUT failed: ${response.status} ${await response.text()}`);
  }
}

function filePayload(source: string, overrides: Record<string, unknown> = {}) {
  return {
    source,
    entityType: 'TestEntity',
    entityId: 'entity-1',
    fileName: 'test.pdf',
    contentType: 'application/pdf',
    sizeBytes: TINY_PDF.length,
    ...overrides,
  };
}

async function initiateUpload(request: APIRequestContext, source: string, overrides: Record<string, unknown> = {}): Promise<InitiateUploadResponse> {
  const response = await request.post('/api/v1/filestore', { data: filePayload(source, overrides) });
  expect(response.status(), `initiateUpload failed: ${await response.text()}`).toBe(201);
  return response.json();
}

async function uploadAndConfirm(request: APIRequestContext, source: string): Promise<InitiateUploadResponse> {
  const initiated = await initiateUpload(request, source);

  await s3Put(initiated.uploadUrl, TINY_PDF, 'application/pdf');

  const confirmed = await request.post(`/api/v1/filestore/${initiated.fileId}/confirm`);
  expect(confirmed.status(), `confirm failed: ${await confirmed.text()}`).toBe(204);

  return initiated;
}

const source = uniqueSource();

test.afterAll(async ({ request }) => {
  await request.delete(`/api/test/filestore?source=${encodeURIComponent(source)}`);
});

test('InitiateUpload_ValidRequest_Returns201WithUploadUrl', async ({ request }) => {
  const response = await request.post('/api/v1/filestore', { data: filePayload(source) });

  expect(response.status()).toBe(201);
  const body: InitiateUploadResponse = await response.json();
  expect(body.fileId).toMatch(/^[0-9a-f-]{36}$/);
  expect(body.uploadUrl).toMatch(/^http/);
  expect(body.uploadExpiresAt).toBeTruthy();
});

test('InitiateUpload_InvalidContentType_Returns400', async ({ request }) => {
  const response = await request.post('/api/v1/filestore', {
    data: filePayload(source, { contentType: 'text/plain' }),
  });
  expect(response.status()).toBe(400);
});

test('UploadAndConfirm_FullLifecycle_FileBecomesConfirmed', async ({ request }) => {
  const { fileId } = await uploadAndConfirm(request, source);

  const getResponse = await request.get(`/api/v1/filestore/${fileId}`);
  expect(getResponse.status()).toBe(200);
  const file: FileDto = await getResponse.json();
  expect(file.status).toBe(1); // Confirmed
  expect(file.confirmedAt).not.toBeNull();
});

test('ListFiles_FilterBySource_ReturnsOnlyMatchingSource', async ({ request }) => {
  const source1 = uniqueSource();
  const source2 = uniqueSource();

  await uploadAndConfirm(request, source1);
  await uploadAndConfirm(request, source2);

  const response = await request.get('/api/v1/filestore', { params: { source: source1 } });
  expect(response.status()).toBe(200);
  const files: FileDto[] = await response.json();
  expect(files.length).toBeGreaterThanOrEqual(1);
  expect(files.every(f => f.source === source1)).toBe(true);

  // Cleanup extra sources
  await request.delete(`/api/test/filestore?source=${encodeURIComponent(source1)}`);
  await request.delete(`/api/test/filestore?source=${encodeURIComponent(source2)}`);
});

test('ListFiles_FilterByEntityType_ReturnsOnlyMatching', async ({ request }) => {
  const s = uniqueSource();
  await uploadAndConfirm(request, s);

  const r1 = await request.post('/api/v1/filestore', { data: filePayload(s, { entityType: 'TypeA' }) });
  const r1Body: InitiateUploadResponse = await r1.json();
  await s3Put(r1Body.uploadUrl, TINY_PDF, 'application/pdf');
  await request.post(`/api/v1/filestore/${r1Body.fileId}/confirm`);

  const r2 = await request.post('/api/v1/filestore', { data: filePayload(s, { entityType: 'TypeB' }) });
  const r2Body: InitiateUploadResponse = await r2.json();
  await s3Put(r2Body.uploadUrl, TINY_PDF, 'application/pdf');
  await request.post(`/api/v1/filestore/${r2Body.fileId}/confirm`);

  const response = await request.get('/api/v1/filestore', { params: { source: s, entityType: 'TypeA' } });
  expect(response.status()).toBe(200);
  const files: FileDto[] = await response.json();
  expect(files.every(f => f.entityType === 'TypeA')).toBe(true);

  await request.delete(`/api/test/filestore?source=${encodeURIComponent(s)}`);
});

test('GetFile_Found_ReturnsFileDto', async ({ request }) => {
  const { fileId } = await uploadAndConfirm(request, source);

  const response = await request.get(`/api/v1/filestore/${fileId}`);
  expect(response.status()).toBe(200);
  const file: FileDto = await response.json();
  expect(file.id).toBe(fileId);
  expect(file.source).toBe(source);
  expect(file.fileName).toBe('test.pdf');
  expect(file.contentType).toBe('application/pdf');
});

test('GetFile_NotFound_Returns404', async ({ request }) => {
  const response = await request.get('/api/v1/filestore/00000000-0000-0000-0000-000000000001');
  expect(response.status()).toBe(404);
});

test('GetFileUrl_ConfirmedFile_ReturnsPresignedUrl', async ({ request }) => {
  const { fileId } = await uploadAndConfirm(request, source);

  const response = await request.get(`/api/v1/filestore/${fileId}/url`);
  expect(response.status()).toBe(200);
  const body: FileUrlResponse = await response.json();
  expect(body.url).toMatch(/^http/);
  expect(new Date(body.expiresAt).getTime()).toBeGreaterThan(Date.now());
});

test('GetFileUrl_PendingFile_Returns404', async ({ request }) => {
  const initiated = await initiateUpload(request, source);

  const response = await request.get(`/api/v1/filestore/${initiated.fileId}/url`);
  expect(response.status()).toBe(404);
});

test('DeleteFile_Returns204AndFileDisappears', async ({ request }) => {
  const { fileId } = await uploadAndConfirm(request, source);

  const deleteResponse = await request.delete(`/api/v1/filestore/${fileId}`);
  expect(deleteResponse.status()).toBe(204);

  const getResponse = await request.get(`/api/v1/filestore/${fileId}`);
  expect(getResponse.status()).toBe(404);
});

test('ConfirmUpload_WithoutActualUpload_Returns422', async ({ request }) => {
  const initiated = await initiateUpload(request, source);

  const response = await request.post(`/api/v1/filestore/${initiated.fileId}/confirm`);
  expect(response.status()).toBe(422);
});

import { test, expect, APIRequestContext } from '@playwright/test';
import type {
  AddVersionResponse,
  CreateDocumentResponse,
  DocumentDto,
  DocumentVersionDto,
  FileUrlResponse,
} from './types';

const BASE = process.env.FILESTORE_BASE_URL ?? 'http://localhost:5300';
const TINY_PDF = Buffer.from('%PDF-1.4 1 0 obj<</Type/Catalog>>endobj');

function uniqueSource(): string {
  return `pw-doc-${crypto.randomUUID().replaceAll('-', '').slice(0, 12)}`;
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

function createDocPayload(source: string, overrides: Record<string, unknown> = {}) {
  return {
    source,
    entityType: 'TestEntity',
    entityId: 'entity-1',
    name: 'Test Document',
    retentionPolicy: 0, // KeepAll
    retentionCount: null,
    fileName: 'doc.pdf',
    contentType: 'application/pdf',
    sizeBytes: TINY_PDF.length,
    comment: null,
    ...overrides,
  };
}

async function createDocument(request: APIRequestContext, source: string, overrides: Record<string, unknown> = {}): Promise<CreateDocumentResponse> {
  const response = await request.post('/api/v1/documents', { data: createDocPayload(source, overrides) });
  expect(response.status(), `createDocument failed: ${await response.text()}`).toBe(201);
  return response.json();
}

async function uploadAndConfirmVersion(
  request: APIRequestContext,
  documentId: string,
  versionId: string,
  uploadUrl: string,
): Promise<void> {
  await s3Put(uploadUrl, TINY_PDF, 'application/pdf');

  const confirmed = await request.post(`/api/v1/documents/${documentId}/versions/${versionId}/confirm`);
  expect(confirmed.status(), `confirmVersion failed: ${await confirmed.text()}`).toBe(204);
}

async function createAndConfirmDocument(request: APIRequestContext, source: string, overrides: Record<string, unknown> = {}): Promise<CreateDocumentResponse> {
  const created = await createDocument(request, source, overrides);
  await uploadAndConfirmVersion(request, created.documentId, created.versionId, created.uploadUrl);
  return created;
}

const source = uniqueSource();

test.afterAll(async ({ request }) => {
  await request.delete(`/api/test/filestore?source=${encodeURIComponent(source)}`);
});

test('CreateDocument_ValidRequest_Returns201WithUploadUrl', async ({ request }) => {
  const response = await request.post('/api/v1/documents', { data: createDocPayload(source) });

  expect(response.status()).toBe(201);
  const body: CreateDocumentResponse = await response.json();
  expect(body.documentId).toMatch(/^[0-9a-f-]{36}$/);
  expect(body.versionId).toMatch(/^[0-9a-f-]{36}$/);
  expect(body.fileId).toMatch(/^[0-9a-f-]{36}$/);
  expect(body.uploadUrl).toMatch(/^http/);
});

test('CreateAndConfirm_FullDocumentLifecycle_VersionIsConfirmed', async ({ request }) => {
  const { documentId, versionId } = await createAndConfirmDocument(request, source);

  const response = await request.get(`/api/v1/documents/${documentId}`);
  expect(response.status()).toBe(200);
  const doc: DocumentDto = await response.json();
  expect(doc.currentVersion).not.toBeNull();
  expect(doc.currentVersion!.id).toBe(versionId);
  expect(doc.currentVersion!.status).toBe(1); // Confirmed
  expect(doc.currentVersion!.versionNumber).toBe(1);
});

test('AddVersion_IncreasesVersionNumber', async ({ request }) => {
  const s = uniqueSource();
  const { documentId } = await createAndConfirmDocument(request, s);

  const addResponse = await request.post(`/api/v1/documents/${documentId}/versions`, {
    data: { fileName: 'v2.pdf', contentType: 'application/pdf', sizeBytes: TINY_PDF.length, comment: 'v2' },
  });
  expect(addResponse.status()).toBe(201);
  const addBody: AddVersionResponse = await addResponse.json();

  await uploadAndConfirmVersion(request, documentId, addBody.versionId, addBody.uploadUrl);

  const versionsResponse = await request.get(`/api/v1/documents/${documentId}/versions`);
  const versions: DocumentVersionDto[] = await versionsResponse.json();
  expect(versions.length).toBe(2);
  expect(versions.some(v => v.versionNumber === 2 && v.status === 1)).toBe(true);

  const docResponse = await request.get(`/api/v1/documents/${documentId}`);
  const doc: DocumentDto = await docResponse.json();
  expect(doc.currentVersion!.versionNumber).toBe(2);

  await request.delete(`/api/test/filestore?source=${encodeURIComponent(s)}`);
});

test('RetentionPolicy_KeepLatest_PrunesOldVersion', async ({ request }) => {
  const s = uniqueSource();
  const { documentId } = await createAndConfirmDocument(request, s, { retentionPolicy: 1 }); // KeepLatest

  const addResponse = await request.post(`/api/v1/documents/${documentId}/versions`, {
    data: { fileName: 'v2.pdf', contentType: 'application/pdf', sizeBytes: TINY_PDF.length, comment: null },
  });
  const addBody: AddVersionResponse = await addResponse.json();
  await uploadAndConfirmVersion(request, documentId, addBody.versionId, addBody.uploadUrl);

  const versionsResponse = await request.get(`/api/v1/documents/${documentId}/versions`);
  const versions: DocumentVersionDto[] = await versionsResponse.json();
  // KeepLatest: only one confirmed version should remain (v1 is pruned/deleted)
  const confirmed = versions.filter(v => v.status === 1);
  expect(confirmed.length).toBe(1);
  expect(confirmed[0].versionNumber).toBe(2);

  await request.delete(`/api/test/filestore?source=${encodeURIComponent(s)}`);
});

test('RetentionPolicy_KeepN_PrunesOldVersionsBeyondCount', async ({ request }) => {
  const s = uniqueSource();
  const { documentId } = await createAndConfirmDocument(request, s, { retentionPolicy: 2, retentionCount: 2 }); // KeepN=2

  for (let i = 2; i <= 3; i++) {
    const addResponse = await request.post(`/api/v1/documents/${documentId}/versions`, {
      data: { fileName: `v${i}.pdf`, contentType: 'application/pdf', sizeBytes: TINY_PDF.length, comment: null },
    });
    const addBody: AddVersionResponse = await addResponse.json();
    await uploadAndConfirmVersion(request, documentId, addBody.versionId, addBody.uploadUrl);
  }

  const versionsResponse = await request.get(`/api/v1/documents/${documentId}/versions`);
  const versions: DocumentVersionDto[] = await versionsResponse.json();
  const confirmed = versions.filter(v => v.status === 1);
  // KeepN=2: only v2 and v3 should be confirmed
  expect(confirmed.length).toBe(2);
  expect(confirmed.every(v => v.versionNumber >= 2)).toBe(true);

  await request.delete(`/api/test/filestore?source=${encodeURIComponent(s)}`);
});

test('ListDocuments_FilterBySource_ReturnsCorrectSubset', async ({ request }) => {
  const s1 = uniqueSource();
  const s2 = uniqueSource();
  await createAndConfirmDocument(request, s1);
  await createAndConfirmDocument(request, s2);

  const response = await request.get('/api/v1/documents', { params: { source: s1 } });
  expect(response.status()).toBe(200);
  const docs: DocumentDto[] = await response.json();
  expect(docs.length).toBeGreaterThanOrEqual(1);
  expect(docs.every(d => d.source === s1)).toBe(true);

  await request.delete(`/api/test/filestore?source=${encodeURIComponent(s1)}`);
  await request.delete(`/api/test/filestore?source=${encodeURIComponent(s2)}`);
});

test('GetDocument_NotFound_Returns404', async ({ request }) => {
  const response = await request.get('/api/v1/documents/00000000-0000-0000-0000-000000000001');
  expect(response.status()).toBe(404);
});

test('GetDocumentUrl_ConfirmedDocument_ReturnsPresignedUrl', async ({ request }) => {
  const { documentId } = await createAndConfirmDocument(request, source);

  const response = await request.get(`/api/v1/documents/${documentId}/url`);
  expect(response.status()).toBe(200);
  const body: FileUrlResponse = await response.json();
  expect(body.url).toMatch(/^http/);
  expect(new Date(body.expiresAt).getTime()).toBeGreaterThan(Date.now());
});

test('GetDocumentUrl_NoConfirmedVersion_Returns404', async ({ request }) => {
  const created = await createDocument(request, source);

  const response = await request.get(`/api/v1/documents/${created.documentId}/url`);
  expect(response.status()).toBe(404);
});

test('GetVersion_Found_ReturnsDto', async ({ request }) => {
  const { documentId, versionId } = await createAndConfirmDocument(request, source);

  const response = await request.get(`/api/v1/documents/${documentId}/versions/${versionId}`);
  expect(response.status()).toBe(200);
  const version: DocumentVersionDto = await response.json();
  expect(version.id).toBe(versionId);
  expect(version.versionNumber).toBe(1);
});

test('GetVersionUrl_ConfirmedVersion_ReturnsUrl', async ({ request }) => {
  const { documentId, versionId } = await createAndConfirmDocument(request, source);

  const response = await request.get(`/api/v1/documents/${documentId}/versions/${versionId}/url`);
  expect(response.status()).toBe(200);
  const body: FileUrlResponse = await response.json();
  expect(body.url).toMatch(/^http/);
});

test('DeleteVersion_CurrentVersion_UpdatesCurrentVersionPointer', async ({ request }) => {
  const s = uniqueSource();
  const { documentId, versionId: v1Id } = await createAndConfirmDocument(request, s);

  const addResponse = await request.post(`/api/v1/documents/${documentId}/versions`, {
    data: { fileName: 'v2.pdf', contentType: 'application/pdf', sizeBytes: TINY_PDF.length, comment: null },
  });
  const addBody: AddVersionResponse = await addResponse.json();
  await uploadAndConfirmVersion(request, documentId, addBody.versionId, addBody.uploadUrl);

  const deleteResponse = await request.delete(`/api/v1/documents/${documentId}/versions/${addBody.versionId}`);
  expect(deleteResponse.status()).toBe(204);

  const docResponse = await request.get(`/api/v1/documents/${documentId}`);
  const doc: DocumentDto = await docResponse.json();
  expect(doc.currentVersion!.id).toBe(v1Id);

  await request.delete(`/api/test/filestore?source=${encodeURIComponent(s)}`);
});

test('DeleteDocument_Returns204AndDocumentDisappears', async ({ request }) => {
  const { documentId } = await createAndConfirmDocument(request, source);

  const deleteResponse = await request.delete(`/api/v1/documents/${documentId}`);
  expect(deleteResponse.status()).toBe(204);

  const getResponse = await request.get(`/api/v1/documents/${documentId}`);
  expect(getResponse.status()).toBe(404);
});

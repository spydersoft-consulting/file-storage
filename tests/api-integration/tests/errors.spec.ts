import { test, expect } from '@playwright/test';
import type { CreateDocumentResponse, InitiateUploadResponse } from './types';

const TINY_PDF = Buffer.from('%PDF-1.4 1 0 obj<</Type/Catalog>>endobj');

function uniqueSource(): string {
  return `pw-err-${crypto.randomUUID().replaceAll('-', '').slice(0, 12)}`;
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

const source = uniqueSource();

test.afterAll(async ({ request }) => {
  await request.delete(`/api/test/filestore?source=${encodeURIComponent(source)}`);
});

test('ConfirmUpload_NonExistentId_Returns404', async ({ request }) => {
  const response = await request.post('/api/v1/filestore/00000000-0000-0000-0000-000000000001/confirm');
  expect(response.status()).toBe(404);
});

test('ConfirmUpload_FileNotUploaded_Returns422', async ({ request }) => {
  const initResponse = await request.post('/api/v1/filestore', {
    data: {
      source,
      entityType: 'TestEntity',
      entityId: 'err-1',
      fileName: 'test.pdf',
      contentType: 'application/pdf',
      sizeBytes: TINY_PDF.length,
    },
  });
  expect(initResponse.status()).toBe(201);
  const { fileId }: InitiateUploadResponse = await initResponse.json();

  // Confirm without uploading to MinIO
  const confirmResponse = await request.post(`/api/v1/filestore/${fileId}/confirm`);
  expect(confirmResponse.status()).toBe(422);
});

test('GetFileUrl_DeletedFile_Returns404', async ({ request }) => {
  // Create, upload, confirm, then delete — URL should 404 after delete
  const initResponse = await request.post('/api/v1/filestore', {
    data: {
      source,
      entityType: 'TestEntity',
      entityId: 'err-2',
      fileName: 'test.pdf',
      contentType: 'application/pdf',
      sizeBytes: TINY_PDF.length,
    },
  });
  const { fileId, uploadUrl }: InitiateUploadResponse = await initResponse.json();
  await s3Put(uploadUrl, TINY_PDF, 'application/pdf');
  await request.post(`/api/v1/filestore/${fileId}/confirm`);
  await request.delete(`/api/v1/filestore/${fileId}`);

  const urlResponse = await request.get(`/api/v1/filestore/${fileId}/url`);
  expect(urlResponse.status()).toBe(404);
});

test('AddVersion_ToNonExistentDocument_Returns404', async ({ request }) => {
  const response = await request.post('/api/v1/documents/00000000-0000-0000-0000-000000000001/versions', {
    data: { fileName: 'test.pdf', contentType: 'application/pdf', sizeBytes: 1, comment: null },
  });
  expect(response.status()).toBe(404);
});

test('ConfirmVersion_FileNotUploaded_Returns422', async ({ request }) => {
  const createResponse = await request.post('/api/v1/documents', {
    data: {
      source,
      entityType: 'TestEntity',
      entityId: 'err-3',
      name: 'Error Test Doc',
      retentionPolicy: 0,
      retentionCount: null,
      fileName: 'doc.pdf',
      contentType: 'application/pdf',
      sizeBytes: TINY_PDF.length,
      comment: null,
    },
  });
  expect(createResponse.status()).toBe(201);
  const { documentId, versionId }: CreateDocumentResponse = await createResponse.json();

  // Confirm without uploading to MinIO
  const confirmResponse = await request.post(`/api/v1/documents/${documentId}/versions/${versionId}/confirm`);
  expect(confirmResponse.status()).toBe(422);
});

test('DeleteVersion_NonExistentVersion_Returns404', async ({ request }) => {
  const createResponse = await request.post('/api/v1/documents', {
    data: {
      source,
      entityType: 'TestEntity',
      entityId: 'err-4',
      name: 'Error Test Doc 2',
      retentionPolicy: 0,
      retentionCount: null,
      fileName: 'doc.pdf',
      contentType: 'application/pdf',
      sizeBytes: TINY_PDF.length,
      comment: null,
    },
  });
  const { documentId }: CreateDocumentResponse = await createResponse.json();

  const response = await request.delete(
    `/api/v1/documents/${documentId}/versions/00000000-0000-0000-0000-000000000001`,
  );
  expect(response.status()).toBe(404);
});

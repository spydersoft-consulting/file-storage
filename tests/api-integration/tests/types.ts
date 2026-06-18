export interface InitiateUploadResponse {
  fileId: string;
  uploadUrl: string;
  uploadExpiresAt: string;
}

export interface FileDto {
  id: string;
  tenantId: string;
  source: string;
  entityType: string;
  entityId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  status: number; // 0=Pending, 1=Confirmed, 2=Deleted
  initiatedAt: string;
  confirmedAt: string | null;
}

export interface FileUrlResponse {
  url: string;
  expiresAt: string;
}

export interface CreateDocumentResponse {
  documentId: string;
  versionId: string;
  fileId: string;
  uploadUrl: string;
  uploadExpiresAt: string;
}

export interface AddVersionResponse {
  versionId: string;
  fileId: string;
  uploadUrl: string;
  uploadExpiresAt: string;
}

export interface DocumentVersionDto {
  id: string;
  documentId: string;
  fileId: string;
  versionNumber: number;
  comment: string | null;
  status: number; // 0=Pending, 1=Confirmed, 2=Deleted
  uploadedAt: string;
  uploadedBy: string;
}

export interface DocumentDto {
  id: string;
  tenantId: string;
  source: string;
  entityType: string;
  entityId: string;
  name: string;
  retentionPolicy: number; // 0=KeepAll, 1=KeepLatest, 2=KeepN
  retentionCount: number | null;
  createdAt: string;
  currentVersion: DocumentVersionDto | null;
}

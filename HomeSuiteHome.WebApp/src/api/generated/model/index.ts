export interface GetDocumentsResponse {
  id: string;
  name: string;
  fileName: string;
  contentType: string;
  fileSize: number;
}

export interface PaginatedResponseOfGetDocumentsResponse {
  items: GetDocumentsResponse[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface GetDocumentsParams {
  page?: number;
  pageSize?: number;
}

export interface CreateDocumentResponse {
  documentId: string;
}

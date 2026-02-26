import { Routes } from '@angular/router';
import { DocumentListComponent } from './pages/document-list/document-list.component';
import { DocumentUploadComponent } from './pages/document-upload/document-upload.component';
import { PartyListComponent } from './pages/party-list/party-list.component';

export const DOCUMENTS_ROUTES: Routes = [
  { path: '', component: DocumentListComponent, data: { title: 'All Documents' } },
  { path: 'upload', component: DocumentUploadComponent, data: { title: 'Upload Document' } },
  { path: 'parties', component: PartyListComponent, data: { title: 'Parties' } },
];

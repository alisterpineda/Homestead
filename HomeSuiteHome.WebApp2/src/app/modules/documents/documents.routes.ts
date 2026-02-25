import { Routes } from '@angular/router';
import { DocumentListComponent } from './pages/document-list/document-list.component';
import { DocumentUploadComponent } from './pages/document-upload/document-upload.component';
import { PartyListComponent } from './pages/party-list/party-list.component';

export const DOCUMENTS_ROUTES: Routes = [
  { path: '', component: DocumentListComponent },
  { path: 'upload', component: DocumentUploadComponent },
  { path: 'parties', component: PartyListComponent },
];

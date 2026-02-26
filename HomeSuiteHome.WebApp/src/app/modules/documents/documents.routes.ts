import { Routes } from '@angular/router';
import { DocumentListComponent } from './pages/document-list/document-list.component';
import { PartyListComponent } from './pages/party-list/party-list.component';
import { DocumentDetailPageComponent } from './pages/document-detail/document-detail-page.component';

export const DOCUMENTS_ROUTES: Routes = [
  { path: '', component: DocumentListComponent, data: { title: 'Documents' } },
  { path: 'parties', component: PartyListComponent, data: { title: 'Parties' } },
  { path: ':id', component: DocumentDetailPageComponent, data: { title: 'Document' } },
];

import { Routes } from '@angular/router';
import { DocumentListComponent } from './pages/document-list/document-list.component';
import { PartyListComponent } from './pages/party-list/party-list.component';
import { PartyDetailPageComponent } from './pages/party-detail/party-detail-page.component';
import { TagListComponent } from './pages/tag-list/tag-list.component';
import { TagDetailPageComponent } from './pages/tag-detail/tag-detail-page.component';
import { DocumentDetailPageComponent } from './pages/document-detail/document-detail-page.component';

export const DOCUMENTS_ROUTES: Routes = [
  { path: '', component: DocumentListComponent, data: { title: 'Documents' } },
  { path: 'parties', component: PartyListComponent, data: { title: 'Parties' } },
  { path: 'parties/:id', component: PartyDetailPageComponent, data: { title: 'Party' } },
  { path: 'tags', component: TagListComponent, data: { title: 'Tags' } },
  { path: 'tags/:id', component: TagDetailPageComponent, data: { title: 'Tag' } },
  { path: ':id/versions/:versionId', component: DocumentDetailPageComponent, data: { title: 'Document' } },
  { path: ':id', component: DocumentDetailPageComponent, data: { title: 'Document' } },
];

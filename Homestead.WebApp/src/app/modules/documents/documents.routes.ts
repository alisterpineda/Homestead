import { Routes } from '@angular/router';
import { DocumentListComponent } from './pages/document-list/document-list.component';
import { PartyListComponent } from './pages/party-list/party-list.component';
import { TagListComponent } from './pages/tag-list/tag-list.component';
import { TrashListComponent } from './pages/trash-list/trash-list.component';
import { DocumentDetailPageComponent } from './pages/document-detail/document-detail-page.component';
import { DocumentsLayoutComponent } from './components/documents-layout/documents-layout.component';

export const DOCUMENTS_ROUTES: Routes = [
  {
    path: '',
    component: DocumentsLayoutComponent,
    children: [
      { path: '', component: DocumentListComponent, data: { title: 'Documents' } },
      { path: 'parties', component: PartyListComponent, data: { title: 'Parties' } },
      { path: 'tags', component: TagListComponent, data: { title: 'Tags' } },
      { path: 'trash', component: TrashListComponent, data: { title: 'Trash' } },
      { path: ':id/versions/:versionId', component: DocumentDetailPageComponent, data: { title: 'Document' } },
      { path: ':id', component: DocumentDetailPageComponent, data: { title: 'Document' } },
    ],
  },
];

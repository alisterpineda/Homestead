import { Routes } from '@angular/router';
import { DocumentListComponent } from './list/document-list.component';
import { PartyListComponent } from './parties/party-list.component';
import { TagListComponent } from './tags/tag-list.component';
import { TrashListComponent } from './trash/trash-list.component';
import { DocumentDetailPageComponent } from './detail/document-detail-page.component';
import { DocumentsLayoutComponent } from './layout/documents-layout.component';

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

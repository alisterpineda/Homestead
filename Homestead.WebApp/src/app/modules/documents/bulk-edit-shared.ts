export const CREATE_OPTION_ID = '__create__';

export type BulkItemState = 'none' | 'checked' | 'mixed' | 'remove';

export interface TagCheckboxItem {
  path: string;
  color: string | null;
  initial: 'all' | 'some' | 'new';
  state: BulkItemState;
}

export interface PartyCheckboxItem {
  id: string;
  name: string;
  initial: 'all' | 'some' | 'new';
  state: BulkItemState;
}

export function nextState(current: BulkItemState, initial: 'all' | 'some' | 'new'): BulkItemState {
  switch (initial) {
    case 'all':
      // checked → remove → checked
      return current === 'checked' ? 'remove' : 'checked';
    case 'some':
      // mixed → checked → remove → mixed
      if (current === 'mixed') return 'checked';
      if (current === 'checked') return 'remove';
      return 'mixed'; // remove → mixed
    case 'new':
      // checked → none → checked
      return current === 'checked' ? 'none' : 'checked';
  }
}

export function isChanged(state: BulkItemState, initial: 'all' | 'some' | 'new'): boolean {
  switch (initial) {
    case 'all':
      return state !== 'checked';
    case 'some':
      return state !== 'mixed';
    case 'new':
      return state === 'checked';
  }
}

export function buildConfirmationMessage(
  addCount: number,
  removeCount: number,
  docCount: number,
  singular: string,
  plural: string,
): string {
  const parts: string[] = [];
  if (addCount > 0) parts.push(`add ${addCount} ${addCount > 1 ? plural : singular}`);
  if (removeCount > 0) parts.push(`remove ${removeCount} ${removeCount > 1 ? plural : singular}`);
  return `This will ${parts.join(' and ')} on ${docCount} document${docCount > 1 ? 's' : ''}.`;
}

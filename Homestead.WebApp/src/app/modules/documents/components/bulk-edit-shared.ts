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

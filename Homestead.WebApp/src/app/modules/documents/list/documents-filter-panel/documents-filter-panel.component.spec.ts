import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DocumentsFilterPanelComponent } from './documents-filter-panel.component';
import { stubMatchMedia } from '@/shared/testing/match-media';

describe('DocumentsFilterPanelComponent', () => {
  let fixture: ComponentFixture<DocumentsFilterPanelComponent>;
  let component: DocumentsFilterPanelComponent;
  let media: ReturnType<typeof stubMatchMedia>;

  beforeEach(() => {
    // injectIsNarrowViewport reads matchMedia at construction, so stub before creating.
    media = stubMatchMedia(true);
    TestBed.configureTestingModule({});
    fixture = TestBed.createComponent(DocumentsFilterPanelComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => vi.unstubAllGlobals());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  describe('triggerLabel', () => {
    it('returns "Filter" when no dimensions are active', () => {
      expect(component.triggerLabel()).toBe('Filter');
      expect(component.activeCount()).toBe(0);
    });

    it('returns "Filter: Tags" when only tags are active', () => {
      fixture.componentRef.setInput('selectedTagIds', ['tag-1']);
      expect(component.triggerLabel()).toBe('Filter: Tags');
      expect(component.activeCount()).toBe(1);
    });

    it('returns "Filter: Tags" when only includeChildTags is set (counts as a tag dimension)', () => {
      fixture.componentRef.setInput('includeChildTags', true);
      expect(component.triggerLabel()).toBe('Filter: Tags');
    });

    it('returns "Filter: Parties" when only parties are active', () => {
      fixture.componentRef.setInput('selectedPartyIds', ['party-1']);
      expect(component.triggerLabel()).toBe('Filter: Parties');
      expect(component.activeCount()).toBe(1);
    });

    it('returns "Filter: Tags, Parties" when both dimensions are active', () => {
      fixture.componentRef.setInput('selectedTagIds', ['tag-1']);
      fixture.componentRef.setInput('selectedPartyIds', ['party-1']);
      expect(component.triggerLabel()).toBe('Filter: Tags, Parties');
      expect(component.activeCount()).toBe(2);
    });
  });

  describe('reset', () => {
    it('emits reset and clears local search inputs', () => {
      const spy = vi.fn();
      component.reset.subscribe(spy);
      component.tagSearch.set('foo');
      component.partySearch.set('bar');

      component.onReset();

      expect(spy).toHaveBeenCalledTimes(1);
      expect(component.tagSearch()).toBe('');
      expect(component.partySearch()).toBe('');
    });
  });

  describe('search handling', () => {
    it('updates local tagSearch and emits tagSearchChange', () => {
      const spy = vi.fn();
      component.tagSearchChange.subscribe(spy);
      component.onTagSearchChange('alpha');
      expect(component.tagSearch()).toBe('alpha');
      expect(spy).toHaveBeenCalledWith('alpha');
    });

    it('updates local partySearch and emits partySearchChange', () => {
      const spy = vi.fn();
      component.partySearchChange.subscribe(spy);
      component.onPartySearchChange('acme');
      expect(component.partySearch()).toBe('acme');
      expect(spy).toHaveBeenCalledWith('acme');
    });
  });

  describe('label lookup helpers', () => {
    it('resolves tag labels from tagOptions first', () => {
      fixture.componentRef.setInput('tagOptions', [{ id: 'a', label: 'Alpha' }]);
      fixture.componentRef.setInput('resolvedTagOptions', [{ id: 'a', label: 'Stale-Alpha' }]);
      expect(component.getTagLabel('a')).toBe('Alpha');
    });

    it('falls back to resolvedTagOptions when not in tagOptions', () => {
      fixture.componentRef.setInput('tagOptions', []);
      fixture.componentRef.setInput('resolvedTagOptions', [{ id: 'r', label: 'Resolved' }]);
      expect(component.getTagLabel('r')).toBe('Resolved');
    });

    it('falls back to tagLabelCache when not in tagOptions or resolvedTagOptions', () => {
      // Populate the cache by surfacing the label in tagOptions once
      fixture.componentRef.setInput('tagOptions', [{ id: 'a', label: 'Alpha' }]);
      fixture.detectChanges();
      // Then narrow tagOptions so the id is no longer in either list
      fixture.componentRef.setInput('tagOptions', [{ id: 'b', label: 'Bravo' }]);
      fixture.componentRef.setInput('resolvedTagOptions', []);
      fixture.detectChanges();
      expect(component.getTagLabel('a')).toBe('Alpha');
    });

    it('falls back to the id itself when no label is known', () => {
      expect(component.getTagLabel('ghost')).toBe('ghost');
    });

    it('resolves party labels via the same precedence', () => {
      fixture.componentRef.setInput('partyOptions', [{ id: 'p1', label: 'Acme' }]);
      expect(component.getPartyLabel('p1')).toBe('Acme');
      expect(component.getPartyLabel('unknown')).toBe('unknown');
    });
  });

  describe('unselected option lists', () => {
    it('excludes selected ids from the tag dropdown list', () => {
      fixture.componentRef.setInput('tagOptions', [
        { id: 'a', label: 'Alpha' },
        { id: 'b', label: 'Bravo' },
        { id: 'c', label: 'Charlie' },
      ]);
      fixture.componentRef.setInput('selectedTagIds', ['b']);
      expect(component.unselectedTagOptions().map(o => o.id)).toEqual(['a', 'c']);
    });

    it('excludes selected ids from the party dropdown list', () => {
      fixture.componentRef.setInput('partyOptions', [
        { id: 'p1', label: 'Acme' },
        { id: 'p2', label: 'Globex' },
      ]);
      fixture.componentRef.setInput('selectedPartyIds', ['p2']);
      expect(component.unselectedPartyOptions().map(o => o.id)).toEqual(['p1']);
    });
  });

  describe('responsive wrapper', () => {
    it('renders the popover wrapper on desktop', () => {
      fixture.detectChanges();
      const host: HTMLElement = fixture.nativeElement;
      expect(host.querySelector('[hlmpopover]')).not.toBeNull();
      expect(host.querySelector('hlm-sheet')).toBeNull();
    });

    it('renders the sheet wrapper on mobile', () => {
      media.fireChange(false);
      fixture.detectChanges();
      const host: HTMLElement = fixture.nativeElement;
      expect(host.querySelector('hlm-sheet')).not.toBeNull();
      expect(host.querySelector('[hlmpopover]')).toBeNull();
    });
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { DocumentsFilterPanelComponent } from './documents-filter-panel.component';
import { HlmSidebarService } from '@/shared/components/spartan/sidebar';

describe('DocumentsFilterPanelComponent', () => {
  let fixture: ComponentFixture<DocumentsFilterPanelComponent>;
  let component: DocumentsFilterPanelComponent;
  let isMobile: ReturnType<typeof signal<boolean>>;

  beforeEach(() => {
    isMobile = signal(false);
    TestBed.configureTestingModule({
      providers: [
        {
          provide: HlmSidebarService,
          useValue: { isMobile },
        },
      ],
    });
    fixture = TestBed.createComponent(DocumentsFilterPanelComponent);
    component = fixture.componentInstance;
  });

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

  describe('selection toggling', () => {
    it('emits the next tag selection when toggling on', () => {
      fixture.componentRef.setInput('selectedTagIds', ['existing']);
      const spy = vi.fn();
      component.tagSelectionChange.subscribe(spy);
      component.onToggleTag('new');
      expect(spy).toHaveBeenCalledWith(['existing', 'new']);
    });

    it('emits the next tag selection when toggling off', () => {
      fixture.componentRef.setInput('selectedTagIds', ['a', 'b']);
      const spy = vi.fn();
      component.tagSelectionChange.subscribe(spy);
      component.onToggleTag('a');
      expect(spy).toHaveBeenCalledWith(['b']);
    });

    it('emits the next party selection when toggling', () => {
      fixture.componentRef.setInput('selectedPartyIds', []);
      const spy = vi.fn();
      component.partySelectionChange.subscribe(spy);
      component.onToggleParty('p1');
      expect(spy).toHaveBeenCalledWith(['p1']);
    });
  });

  describe('pinned selected items', () => {
    it('partitions selected and unselected from the current options', () => {
      fixture.componentRef.setInput('tagOptions', [
        { id: 'a', label: 'Alpha' },
        { id: 'b', label: 'Bravo' },
        { id: 'c', label: 'Charlie' },
      ]);
      fixture.componentRef.setInput('selectedTagIds', ['b']);
      const display = component.displayTagOptions();
      expect(display.selected.map(o => o.id)).toEqual(['b']);
      expect(display.unselected.map(o => o.id)).toEqual(['a', 'c']);
    });

    it('keeps selected items pinned using a cached label after they leave the current results', () => {
      // First fetch (no search) populates the cache for "alpha"
      fixture.componentRef.setInput('tagOptions', [
        { id: 'a', label: 'Alpha' },
        { id: 'b', label: 'Bravo' },
      ]);
      fixture.componentRef.setInput('selectedTagIds', ['a']);
      fixture.detectChanges();

      // Search narrows to "bravo" only — but "a" should stay pinned via cache
      fixture.componentRef.setInput('tagOptions', [{ id: 'b', label: 'Bravo' }]);
      fixture.detectChanges();

      const display = component.displayTagOptions();
      expect(display.selected).toEqual([{ id: 'a', label: 'Alpha' }]);
      expect(display.unselected.map(o => o.id)).toEqual(['b']);
    });

    it('drops selected items whose label has never been observed', () => {
      // Cache empty; selected id "ghost" was never seen in any tagOptions response
      fixture.componentRef.setInput('tagOptions', [{ id: 'b', label: 'Bravo' }]);
      fixture.componentRef.setInput('selectedTagIds', ['ghost']);
      fixture.detectChanges();

      const display = component.displayTagOptions();
      expect(display.selected).toEqual([]);
      expect(display.unselected.map(o => o.id)).toEqual(['b']);
    });

    it('partitions parties the same way', () => {
      fixture.componentRef.setInput('partyOptions', [
        { id: 'p1', label: 'Acme' },
        { id: 'p2', label: 'Globex' },
      ]);
      fixture.componentRef.setInput('selectedPartyIds', ['p2']);
      fixture.detectChanges();

      const display = component.displayPartyOptions();
      expect(display.selected.map(o => o.id)).toEqual(['p2']);
      expect(display.unselected.map(o => o.id)).toEqual(['p1']);
    });
  });

  describe('responsive wrapper', () => {
    it('renders the popover wrapper on desktop', () => {
      isMobile.set(false);
      fixture.detectChanges();
      const host: HTMLElement = fixture.nativeElement;
      expect(host.querySelector('[hlmpopover]')).not.toBeNull();
      expect(host.querySelector('hlm-sheet')).toBeNull();
    });

    it('renders the sheet wrapper on mobile', () => {
      isMobile.set(true);
      fixture.detectChanges();
      const host: HTMLElement = fixture.nativeElement;
      expect(host.querySelector('hlm-sheet')).not.toBeNull();
      expect(host.querySelector('[hlmpopover]')).toBeNull();
    });
  });
});

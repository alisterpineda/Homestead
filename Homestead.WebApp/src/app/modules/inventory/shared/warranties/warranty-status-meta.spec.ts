import {
  WARRANTY_FACETS,
  WARRANTY_STATUS_META,
  isWarrantyFacet,
  warrantyBadge,
  warrantyExpiryText,
  warrantyFacetFromParam,
  warrantyProviderLabel,
} from './warranty-status-meta';

describe('warrantyBadge', () => {
  it('renders a pill only for the two states that want the eye', () => {
    expect(warrantyBadge('Expiring')?.label).toBe('Expiring soon');
    expect(warrantyBadge('Expired')?.label).toBe('Expired');
    expect(warrantyBadge('Active')).toBeNull();
    expect(warrantyBadge('None')).toBeNull();
  });

  it('gives Expiring the amber act-soon tone and Expired the quiet muted one', () => {
    expect(WARRANTY_STATUS_META.Expiring.badgeClass).toContain('amber');
    expect(WARRANTY_STATUS_META.Expired.badgeClass).toContain('text-muted-foreground');
  });
});

describe('warrantyProviderLabel', () => {
  it('shows a stored provider verbatim', () => {
    expect(warrantyProviderLabel('Home Depot', 'Bosch')).toBe('Home Depot');
  });

  it('derives the manufacturer from the item brand when the provider is null', () => {
    expect(warrantyProviderLabel(null, 'Bosch')).toBe('Bosch · manufacturer');
  });

  it('falls back to the bare word with no brand on the item', () => {
    expect(warrantyProviderLabel(null, null)).toBe('Manufacturer');
    expect(warrantyProviderLabel('   ', '  ')).toBe('Manufacturer');
  });
});

describe('warrantyExpiryText', () => {
  it('reads "no expiry" for an open-ended row', () => {
    expect(warrantyExpiryText(null, 'Active', 'en-US')).toBe('no expiry');
  });

  it('takes its tense from the server-derived status, not a client date compare', () => {
    expect(warrantyExpiryText('2028-03-12', 'Active', 'en-US')).toBe('until Mar 12, 2028');
    expect(warrantyExpiryText('2028-03-12', 'Expiring', 'en-US')).toBe('until Mar 12, 2028');
    expect(warrantyExpiryText('2027-03-12', 'Expired', 'en-US')).toBe('expired Mar 12, 2027');
  });
});

describe('warranty facets', () => {
  it('lists the five single-choice options in filter order', () => {
    expect(WARRANTY_FACETS.map(f => f.value)).toEqual(['any', 'active', 'expiring', 'expired', 'none']);
    expect(WARRANTY_FACETS.map(f => f.label)).toEqual(['Any', 'Active', 'Expiring soon', 'Expired', 'None']);
  });

  it('maps every facet but "any" to the row status it keeps', () => {
    expect(WARRANTY_FACETS.find(f => f.value === 'any')!.status).toBeNull();
    expect(WARRANTY_FACETS.find(f => f.value === 'expiring')!.status).toBe('Expiring');
  });

  it('validates the deep-link param, falling back to "any"', () => {
    expect(warrantyFacetFromParam('expiring')).toBe('expiring');
    expect(warrantyFacetFromParam('nonsense')).toBe('any');
    expect(warrantyFacetFromParam(null)).toBe('any');
    expect(isWarrantyFacet('none')).toBe(true);
    expect(isWarrantyFacet('Expiring')).toBe(false);
  });
});

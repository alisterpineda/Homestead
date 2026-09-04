import { MAX_SUGGESTIONS, matchSubjects } from './subject-suggestions';

describe('matchSubjects', () => {
  const subjects = ['Oil change', 'Oil filter', 'Tyres', 'Brakes', 'Battery', 'Coolant', 'Wipers', 'Air filter', 'Spark plugs'];

  it('returns the first MAX_SUGGESTIONS when nothing is typed', () => {
    expect(matchSubjects(subjects, '')).toEqual(subjects.slice(0, MAX_SUGGESTIONS));
    expect(matchSubjects(subjects, '   ')).toHaveLength(MAX_SUGGESTIONS);
  });

  it('matches case-insensitively anywhere in the subject', () => {
    expect(matchSubjects(subjects, 'OIL')).toEqual(['Oil change', 'Oil filter']);
    expect(matchSubjects(subjects, 'filter')).toEqual(['Oil filter', 'Air filter']);
  });

  it('caps a filtered list too', () => {
    const many = Array.from({ length: 12 }, (_, i) => `Service ${i}`);
    expect(matchSubjects(many, 'service')).toHaveLength(MAX_SUGGESTIONS);
  });

  it('does not mutate the input', () => {
    const copy = [...subjects];
    matchSubjects(subjects, '');
    expect(subjects).toEqual(copy);
  });
});

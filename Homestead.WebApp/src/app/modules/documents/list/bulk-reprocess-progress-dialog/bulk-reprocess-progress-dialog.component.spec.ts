import { TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach } from 'vitest';
import { BulkReprocessProgressDialogComponent } from './bulk-reprocess-progress-dialog.component';

describe('BulkReprocessProgressDialogComponent', () => {
  let component: BulkReprocessProgressDialogComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(BulkReprocessProgressDialogComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should be idle initially', () => {
    expect(component.status()).toBe('idle');
  });

  describe('start', () => {
    it('should set in-progress status and document count', () => {
      component.start(5);

      expect(component.status()).toBe('in-progress');
      expect(component.total()).toBe(5);
      expect(component.completed()).toBe(0);
      expect(component.failed()).toBe(0);
    });

    it('should reset counts from a previous run', () => {
      component.start(3);
      component.updateProgress(2, 1, 3);
      component.markCompleted();

      component.start(10);

      expect(component.total()).toBe(10);
      expect(component.completed()).toBe(0);
      expect(component.failed()).toBe(0);
      expect(component.status()).toBe('in-progress');
    });
  });

  describe('updateProgress', () => {
    it('should update completed and failed counts', () => {
      component.start(5);
      component.updateProgress(3, 1, 5);

      expect(component.completed()).toBe(3);
      expect(component.failed()).toBe(1);
      expect(component.total()).toBe(5);
    });
  });

  describe('progressPercent', () => {
    it('should return 0 when total is 0', () => {
      expect(component.progressPercent()).toBe(0);
    });

    it('should compute percentage from completed + failed', () => {
      component.start(10);
      component.updateProgress(3, 2, 10);

      expect(component.progressPercent()).toBe(50);
    });

    it('should return 100 when all done', () => {
      component.start(4);
      component.updateProgress(4, 0, 4);

      expect(component.progressPercent()).toBe(100);
    });

    it('should round to nearest integer', () => {
      component.start(3);
      component.updateProgress(1, 0, 3);

      expect(component.progressPercent()).toBe(33);
    });
  });

  describe('progressText', () => {
    it('should show 0 of N initially', () => {
      component.start(5);
      expect(component.progressText()).toBe('0 of 5 processed');
    });

    it('should include both completed and failed in count', () => {
      component.start(10);
      component.updateProgress(6, 2, 10);

      expect(component.progressText()).toBe('8 of 10 processed');
    });
  });

  describe('resultMessage', () => {
    it('should show success for all completed', () => {
      component.start(3);
      component.updateProgress(3, 0, 3);

      expect(component.resultMessage()).toBe('Text extraction completed for 3 documents.');
    });

    it('should use singular for 1 document', () => {
      component.start(1);
      component.updateProgress(1, 0, 1);

      expect(component.resultMessage()).toBe('Text extraction completed for 1 document.');
    });

    it('should show failure for all failed', () => {
      component.start(2);
      component.updateProgress(0, 2, 2);

      expect(component.resultMessage()).toBe('Text extraction failed for all 2 documents.');
    });

    it('should show partial for mixed results', () => {
      component.start(5);
      component.updateProgress(3, 2, 5);

      expect(component.resultMessage()).toBe(
        'Text extraction completed for 3 of 5 documents (2 failed).',
      );
    });
  });

  describe('status transitions', () => {
    it('markCompleted sets completed status', () => {
      component.start(1);
      component.markCompleted();
      expect(component.status()).toBe('completed');
    });

    it('markPartiallyCompleted sets partially-completed status', () => {
      component.start(1);
      component.markPartiallyCompleted();
      expect(component.status()).toBe('partially-completed');
    });

    it('markFailed sets failed status', () => {
      component.start(1);
      component.markFailed();
      expect(component.status()).toBe('failed');
    });

    it('closing dialog resets to idle', () => {
      component.start(1);
      component.markCompleted();
      component.onStateChanged('closed');
      expect(component.status()).toBe('idle');
    });
  });
});

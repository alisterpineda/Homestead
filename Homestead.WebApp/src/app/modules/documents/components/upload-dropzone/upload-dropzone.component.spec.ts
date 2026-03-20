import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UploadDropzoneComponent } from './upload-dropzone.component';

describe('UploadDropzoneComponent', () => {
  let fixture: ComponentFixture<UploadDropzoneComponent>;
  let component: UploadDropzoneComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    fixture = TestBed.createComponent(UploadDropzoneComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  function createDragEvent(type: string, file?: File) {
    const files = file ? [file] : [];
    return {
      type,
      preventDefault: vi.fn(),
      stopPropagation: vi.fn(),
      dataTransfer: {
        types: file ? ['Files'] : [],
        files: Object.assign(files, { item: (i: number) => files[i] ?? null }),
      },
    } as unknown as DragEvent;
  }

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should set dragOver on dragover', () => {
    component.onDragOver(createDragEvent('dragover'));
    expect(component.dragOver()).toBe(true);
  });

  it('should clear dragOver on dragleave', () => {
    component.onDragOver(createDragEvent('dragover'));
    component.onDragLeave(createDragEvent('dragleave'));
    expect(component.dragOver()).toBe(false);
  });

  it('should emit fileSelect on drop', () => {
    const file = new File(['content'], 'test.pdf');
    const emitted: File[] = [];
    component.fileSelect.subscribe(f => emitted.push(f));

    component.onDrop(createDragEvent('drop', file));

    expect(emitted).toEqual([file]);
  });

  it('should call stopPropagation on drop', () => {
    const file = new File(['content'], 'test.pdf');
    const event = createDragEvent('drop', file);

    component.onDrop(event);

    expect(event.stopPropagation).toHaveBeenCalled();
  });

  it('should call stopPropagation on dragover', () => {
    const event = createDragEvent('dragover');

    component.onDragOver(event);

    expect(event.stopPropagation).toHaveBeenCalled();
  });

  it('should emit fileSelect on file input change', () => {
    const file = new File(['content'], 'test.pdf');
    const emitted: File[] = [];
    component.fileSelect.subscribe(f => emitted.push(f));

    const event = { target: { files: [file] } } as unknown as Event;
    component.onFileChange(event);

    expect(emitted).toEqual([file]);
  });

  it('should not emit fileSelect on drop without file', () => {
    const emitted: File[] = [];
    component.fileSelect.subscribe(f => emitted.push(f));

    component.onDrop(createDragEvent('drop'));

    expect(emitted).toEqual([]);
  });
});

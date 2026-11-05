import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FileDrop } from './file-drop';

const pdf = (name = 'au.pdf', size = 2048) => new File([new Uint8Array(size)], name, { type: 'application/pdf' });

/** jsdom has no DragEvent: a plain event with the `dataTransfer` the component reads. */
function dragEvent(type: string, files: File[] = []): Event {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'dataTransfer', { value: { files, dropEffect: 'none' } });
  return event;
}

describe('FileDrop', () => {
  let fixture: ComponentFixture<FileDrop>;
  let root: HTMLElement;
  let chosen: File[];
  let cleared: number;

  async function create(file: File | null = null, disabled = false): Promise<void> {
    fixture = TestBed.createComponent(FileDrop);
    fixture.componentRef.setInput('inputId', 'the-file');
    fixture.componentRef.setInput('accept', '.pdf');
    fixture.componentRef.setInput('file', file);
    fixture.componentRef.setInput('disabled', disabled);
    chosen = [];
    cleared = 0;
    fixture.componentInstance.chosen.subscribe((f) => chosen.push(f));
    fixture.componentInstance.cleared.subscribe(() => cleared++);
    root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  }

  const zone = () => root.querySelector<HTMLElement>('[data-testid="file-drop"]')!;
  const input = () => root.querySelector<HTMLInputElement>('#the-file')!;

  it('has a hidden file input with the given id and the accepted types', async () => {
    await create();

    expect(input().type).toBe('file');
    expect(input().getAttribute('accept')).toBe('.pdf');
    expect(input().classList.contains('hidden')).toBe(true);
  });

  it('opens the file chooser with the button', async () => {
    await create();
    const click = vi.spyOn(input(), 'click');

    root.querySelector<HTMLButtonElement>('[data-testid="choose-file"]')!.click();

    expect(click).toHaveBeenCalled();
  });

  it('hands over the file that was chosen and lets the same file be chosen again', async () => {
    await create();
    const file = pdf();
    Object.defineProperty(input(), 'files', { value: [file], configurable: true });

    input().dispatchEvent(new Event('change', { bubbles: true }));

    expect(chosen).toEqual([file]);
  });

  it('takes a dropped file and does not let the browser open it', async () => {
    await create();
    const file = pdf();
    const over = dragEvent('dragover');
    const drop = dragEvent('drop', [file]);

    zone().dispatchEvent(over);
    zone().dispatchEvent(drop);

    expect(over.defaultPrevented).toBe(true);
    expect(drop.defaultPrevented).toBe(true);
    expect(chosen).toEqual([file]);
  });

  it('highlights the zone while a file is dragged over it and clears it when it leaves or drops', async () => {
    await create();

    zone().dispatchEvent(dragEvent('dragover'));
    await fixture.whenStable();
    expect(zone().classList.contains('border-primary')).toBe(true);

    zone().dispatchEvent(dragEvent('dragleave'));
    await fixture.whenStable();
    expect(zone().classList.contains('border-primary')).toBe(false);

    zone().dispatchEvent(dragEvent('dragover'));
    zone().dispatchEvent(dragEvent('drop', [pdf()]));
    await fixture.whenStable();
    expect(zone().classList.contains('border-primary')).toBe(false);
  });

  it('ignores a drop without a file', async () => {
    await create();

    zone().dispatchEvent(dragEvent('drop', []));

    expect(chosen).toEqual([]);
  });

  it('takes nothing while it is disabled, but still keeps the browser from opening the file', async () => {
    await create(null, true);
    const drop = dragEvent('drop', [pdf()]);

    zone().dispatchEvent(drop);

    expect(chosen).toEqual([]);
    expect(drop.defaultPrevented).toBe(true);
    expect(root.querySelector<HTMLButtonElement>('[data-testid="choose-file"]')!.disabled).toBe(true);
  });

  it('shows the chosen file with its size and can drop it again', async () => {
    await create(pdf('Krankschein.pdf', 1536));

    expect(root.querySelector('[data-testid="file-name"]')!.textContent).toContain('Krankschein.pdf (1,5 KB)');
    root.querySelector<HTMLButtonElement>('[data-testid="remove-file"]')!.click();

    expect(cleared).toBe(1);
  });
});

import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
import { Mock, vi } from 'vitest';
import { GifApiService } from '../../services/gif-api.service';
import { GifBrowserComponent } from './gif-browser.component';

describe('GifBrowserComponent', () => {
  let fixture: ComponentFixture<GifBrowserComponent>;
  let root: HTMLElement;
  let trending: Subject<string[]>;
  let search: Subject<string[]>;
  let api: {
    getTrending: Mock<() => Observable<string[]>>;
    search: Mock<(term: string) => Observable<string[]>>;
  };

  beforeEach(async () => {
    // Subjects control response timing without real HTTP requests or delays.
    trending = new Subject<string[]>();
    search = new Subject<string[]>();
    api = {
      getTrending: vi.fn<() => Observable<string[]>>(() => trending),
      search: vi.fn<(term: string) => Observable<string[]>>(() => search)
    };
    await TestBed.configureTestingModule({
      imports: [GifBrowserComponent],
      providers: [{ provide: GifApiService, useValue: api }]
    }).compileComponents();
    fixture = TestBed.createComponent(GifBrowserComponent);
    root = fixture.nativeElement;
    await fixture.whenStable();
  });

  async function submitSearch(term: string): Promise<void> {
    const input = root.querySelector<HTMLInputElement>('#search-term')!;
    input.value = term;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await fixture.whenStable();
    root.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
  }

  async function complete(request: Subject<string[]>, urls: string[]): Promise<void> {
    request.next(urls);
    request.complete();
    await fixture.whenStable();
  }

  it('loads trending on entry and shows loading until the response arrives', async () => {
    expect(api.getTrending).toHaveBeenCalledTimes(1);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('Loading GIFs');
    expect(root.querySelector('section')?.getAttribute('aria-busy')).toBe('true');

    const urls = ['https://example.test/one.gif', 'https://example.test/two.gif'];
    await complete(trending, urls);

    expect([...root.querySelectorAll('.gallery img')].map(img => img.getAttribute('src'))).toEqual(urls);
    expect(root.querySelector('[role="status"]')?.textContent).toContain('2 GIFs loaded');
    expect(root.querySelector('section')?.getAttribute('aria-busy')).toBe('false');
  });

  it('submits the trimmed search term and displays returned images', async () => {
    await complete(trending, []);
    await submitSearch('  cats & dogs  ');
    expect(api.search).toHaveBeenCalledExactlyOnceWith('cats & dogs');
    expect(root.querySelector('h2')?.textContent).toBe('Results for "cats & dogs"');
    expect(root.textContent).toContain('Loading GIFs');
    await complete(search, ['https://example.test/cat.gif']);
    expect(root.querySelector('.gallery img')?.getAttribute('src')).toBe('https://example.test/cat.gif');
  });

  it.each(['', '   '])('rejects a blank search term: %j', async term => {
    await complete(trending, []);
    await submitSearch(term);
    expect(root.querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    expect(api.search).not.toHaveBeenCalled();
  });

  it('loads trending again when its button is clicked after a search', async () => {
    await complete(trending, []);
    await submitSearch('cats');
    await complete(search, ['https://example.test/search.gif']);
    const nextTrending = new Subject<string[]>();
    api.getTrending.mockReturnValueOnce(nextTrending);
    root.querySelector<HTMLButtonElement>('button[type="button"]')!.click();
    await fixture.whenStable();
    expect(api.getTrending).toHaveBeenCalledTimes(2);
    expect(root.querySelector('h2')?.textContent).toBe('Trending GIFs');
    await complete(nextTrending, ['https://example.test/trending.gif']);
    expect(root.querySelector('.gallery img')?.getAttribute('src')).toBe('https://example.test/trending.gif');
  });

  it('shows an empty-result message without an error', async () => {
    await complete(trending, []);
    expect(root.textContent).toContain('No GIFs found');
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(root.querySelector('.gallery')).toBeNull();
  });

  it.each([
    { status: 0, message: 'Unable to reach the API.' },
    { status: 400, message: 'The search term is invalid.' },
    { status: 502, message: 'The GIF provider is unavailable.' },
    { status: 504, message: 'The GIF provider took too long to respond.' },
    { status: 500, message: 'Unable to load GIFs.' }
  ])('shows a safe message for HTTP status $status', async ({ status, message }) => {
    trending.error(new HttpErrorResponse({ status, error: { detail: 'Private diagnostic information' } }));
    await fixture.whenStable();
    expect(root.querySelector('[role="alert"]')?.textContent).toContain(message);
    expect(root.textContent).not.toContain('Private diagnostic information');
    expect(root.querySelector('section')?.getAttribute('aria-busy')).toBe('false');
  });

  it('clears a previous error when a new request succeeds', async () => {
    trending.error(new HttpErrorResponse({ status: 502 }));
    await fixture.whenStable();
    expect(root.querySelector('[role="alert"]')).not.toBeNull();
    await submitSearch('cats');
    expect(root.querySelector('[role="alert"]')).toBeNull();
    await complete(search, ['https://example.test/recovered.gif']);
    expect(root.querySelector('.gallery img')?.getAttribute('src')).toBe('https://example.test/recovered.gif');
  });

  // A late response from a cancelled request must not replace newer results.
  it('cancels an older search and ignores its late result', async () => {
    await complete(trending, []);
    await submitSearch('cats');
    expect(search.observed).toBe(true);
    const latest = new Subject<string[]>();
    api.search.mockReturnValueOnce(latest);
    await submitSearch('dogs');
    expect(search.observed).toBe(false);
    expect(root.querySelector('section')?.getAttribute('aria-busy')).toBe('true');
    await complete(latest, ['https://example.test/dog.gif']);
    search.next(['https://example.test/stale-cat.gif']);
    await fixture.whenStable();
    expect(root.querySelector('.gallery img')?.getAttribute('src')).toBe('https://example.test/dog.gif');
    expect(root.querySelector('h2')?.textContent).toBe('Results for "dogs"');
  });

  it('cancels pending trending when a search starts', async () => {
    expect(trending.observed).toBe(true);
    await submitSearch('cats');
    expect(trending.observed).toBe(false);
    await complete(search, ['https://example.test/cat.gif']);
    trending.next(['https://example.test/stale-trending.gif']);
    await fixture.whenStable();
    expect(root.querySelector('.gallery img')?.getAttribute('src')).toBe('https://example.test/cat.gif');
  });

  it('unsubscribes from the active request when the page is destroyed', () => {
    expect(trending.observed).toBe(true);
    fixture.destroy();
    expect(trending.observed).toBe(false);
  });
});

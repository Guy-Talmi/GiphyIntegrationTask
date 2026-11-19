import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { App } from './app';
import { routes } from './app.routes';
import { GifApiService } from './services/gif-api.service';

describe('App routing', () => {
  // Exercise the real app shell and routes with a fake API service.
  it.each(['/', '/unknown-page'])('displays the GIF page when navigating to %s', async path => {
    const api = {
      getTrending: vi.fn(() => of(['https://example.test/trending.gif'])),
      search: vi.fn(() => of([]))
    };
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), { provide: GifApiService, useValue: api }]
    }).compileComponents();

    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    await router.navigateByUrl(path);
    await fixture.whenStable();

    const root: HTMLElement = fixture.nativeElement;
    expect(router.url).toBe('/');
    expect(root.querySelector('router-outlet')).not.toBeNull();
    expect(root.querySelector('h1')?.textContent).toBe('GIF Explorer');
    expect(root.querySelector('.gallery img')?.getAttribute('src'))
      .toBe('https://example.test/trending.gif');
    expect(api.getTrending).toHaveBeenCalledTimes(1);
  });
});

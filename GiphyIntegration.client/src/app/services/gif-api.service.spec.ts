import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { GifApiService } from './gif-api.service';

describe('GifApiService', () => {
  let service: GifApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(GifApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // Requests are intercepted in memory; no API server or API key is needed.
  it('fetches trending GIF URLs with GET', async () => {
    const result = firstValueFrom(service.getTrending());
    const request = http.expectOne('/api/GiphyIntegration/trending');
    expect(request.request.method).toBe('GET');
    const urls = ['https://example.test/trending.gif'];
    request.flush(urls);
    expect(await result).toEqual(urls);
  });

  it.each(['cats', 'happy cats', 'cats & dogs', 'cats+dogs?'])
    ('sends one correctly encoded search parameter: %s', async term => {
      const result = firstValueFrom(service.search(term));
      const request = http.expectOne(req => req.url === '/api/GiphyIntegration/search');
      expect(request.request.method).toBe('GET');
      const url = new URL(request.request.urlWithParams, 'http://localhost');
      expect([...url.searchParams.entries()]).toEqual([['q', term]]);
      const urls = ['https://example.test/search.gif'];
      request.flush(urls);
      expect(await result).toEqual(urls);
    });

  it('returns an empty result without treating it as an error', async () => {
    const result = firstValueFrom(service.search('no matches'));
    http.expectOne(req => req.url === '/api/GiphyIntegration/search').flush([]);
    expect(await result).toEqual([]);
  });

  it.each([400, 502, 504, 500])('preserves HTTP status %i for the component', async status => {
    const result = firstValueFrom(service.search('cats'));
    const assertion = expect(result).rejects.toMatchObject({ status });
    http.expectOne(req => req.url === '/api/GiphyIntegration/search')
      .flush({ title: 'Request failed' }, { status, statusText: 'Error' });
    await assertion;
  });

  it('cancels the HTTP request when the subscriber unsubscribes', () => {
    const subscription = service.getTrending().subscribe();
    const request = http.expectOne('/api/GiphyIntegration/trending');
    subscription.unsubscribe();
    expect(request.cancelled).toBe(true);
  });
});

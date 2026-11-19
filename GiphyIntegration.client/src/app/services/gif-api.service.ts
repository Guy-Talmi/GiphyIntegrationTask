import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class GifApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/GiphyIntegration';

  getTrending(): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/trending`);
  }

  search(searchTerm: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/search`, {
      params: { q: searchTerm }
    });
  }
}
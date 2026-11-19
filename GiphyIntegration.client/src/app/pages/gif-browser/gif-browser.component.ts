import {
  ChangeDetectionStrategy,
  Component,
  inject,
  OnDestroy,
  OnInit,
  signal
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize, Observable, Subscription } from 'rxjs';
import { GifApiService } from '../../services/gif-api.service';

@Component({
  selector: 'app-gif-browser',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './gif-browser.component.html',
  styleUrl: './gif-browser.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class GifBrowserComponent implements OnInit, OnDestroy {
  private readonly api = inject(GifApiService);
  private activeRequest?: Subscription;

  searchTerm = '';

  readonly gifs = signal<string[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly heading = signal('Trending GIFs');

  ngOnInit(): void {
    this.showTrending();
  }

  showTrending(): void {
    this.load(this.api.getTrending(), 'Trending GIFs');
  }

  search(): void {
    const term = this.searchTerm.trim();

    if (!term) {
      return;
    }

    this.load(this.api.search(term), `Results for "${term}"`);
  }

  ngOnDestroy(): void {
    this.activeRequest?.unsubscribe();
  }

  private load(request: Observable<string[]>, heading: string): void {
    // Cancel the previous request before changing the page state.
    this.activeRequest?.unsubscribe();

    this.heading.set(heading);
    this.gifs.set([]);
    this.error.set('');
    this.loading.set(true);

    this.activeRequest = request
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: urls => this.gifs.set(urls),
        error: (error: HttpErrorResponse) => {
          this.error.set(this.getErrorMessage(error));
        }
      });
  }

  private getErrorMessage(error: HttpErrorResponse): string {
    switch (error.status) {
      case 0:
        return 'Unable to reach the API. Check that the server is running.';
      case 400:
        return 'The search term is invalid. Try a shorter, non-empty term.';
      case 502:
        return 'The GIF provider is unavailable. Please try again.';
      case 504:
        return 'The GIF provider took too long to respond. Please try again.';
      default:
        return 'Unable to load GIFs. Please try again.';
    }
  }
}
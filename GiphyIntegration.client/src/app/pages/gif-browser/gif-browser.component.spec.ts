import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GifBrowserComponent } from './gif-browser.component';

describe('GifBrowserComponent', () => {
  let component: GifBrowserComponent;
  let fixture: ComponentFixture<GifBrowserComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GifBrowserComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(GifBrowserComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

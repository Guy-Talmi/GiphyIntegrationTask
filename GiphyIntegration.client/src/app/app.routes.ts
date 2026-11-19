import { Routes } from '@angular/router';

export const routes: Routes = [
    {
        path: '',
        pathMatch: 'full',
        loadComponent: () =>
            import('./pages/gif-browser/gif-browser.component')
                .then(module => module.GifBrowserComponent)
    },
    {
        path: '**',
        redirectTo: ''
    }
];
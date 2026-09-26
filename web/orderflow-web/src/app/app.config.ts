import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { HTTP_INTERCEPTORS, provideHttpClient, withFetch, withInterceptorsFromDi } from '@angular/common/http';
import {
  BrowserCacheLocation, InteractionType, IPublicClientApplication, PublicClientApplication,
} from '@azure/msal-browser';
import {
  MSAL_GUARD_CONFIG, MSAL_INSTANCE, MSAL_INTERCEPTOR_CONFIG, MsalBroadcastService, MsalGuard,
  MsalGuardConfiguration, MsalInterceptor, MsalInterceptorConfiguration, MsalService,
} from '@azure/msal-angular';
import { environment } from '../environments/environment';

function msalInstanceFactory(): IPublicClientApplication {
  return new PublicClientApplication({
    auth: {
      clientId: environment.auth.clientId,
      authority: `https://login.microsoftonline.com/${environment.auth.tenantId}`,
      redirectUri: window.location.origin,              // must be a registered SPA redirect URI
      postLogoutRedirectUri: window.location.origin,
    },
    // sessionStorage: tokens die with the tab. localStorage would outlive it, giving any injected
    // script (XSS) a longer-lived prize.
    cache: { cacheLocation: BrowserCacheLocation.SessionStorage },
  });
}

// The interceptor attaches a token ONLY to URLs in this map — every other request goes out without
// one, so the API token never leaks to a third-party host.
function msalInterceptorConfigFactory(): MsalInterceptorConfiguration {
  return {
    interactionType: InteractionType.Redirect,
    protectedResourceMap: new Map([[`${environment.apiBaseUrl}/*`, [environment.auth.apiScope]]]),
  };
}

function msalGuardConfigFactory(): MsalGuardConfiguration {
  return { interactionType: InteractionType.Redirect, authRequest: { scopes: [environment.auth.apiScope] } };
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptorsFromDi(), withFetch()),
    { provide: HTTP_INTERCEPTORS, useClass: MsalInterceptor, multi: true },
    { provide: MSAL_INSTANCE, useFactory: msalInstanceFactory },
    { provide: MSAL_GUARD_CONFIG, useFactory: msalGuardConfigFactory },
    { provide: MSAL_INTERCEPTOR_CONFIG, useFactory: msalInterceptorConfigFactory },
    MsalService,
    MsalGuard,
    MsalBroadcastService,
  ],
};

import { APP_INITIALIZER, ApplicationConfig, isDevMode, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideServiceWorker } from '@angular/service-worker';
import { provideNativeDateAdapter } from '@spartan-ng/brain/date-time';
import { provideEchartsCore } from 'ngx-echarts';

import { routes } from './app.routes';
import { AuthService } from '@/shared/services/auth.service';
import { authInterceptor } from '@/shared/interceptors/auth.interceptor';
import { errorInterceptor } from '@/shared/interceptors/error.interceptor';
import { loadEcharts } from '@/shared/utils/echarts-core';
import { formatPickerDate, parsePickerDate } from '@/shared/utils/date-picker-config';
// Deep import (not the date-picker barrel): the barrel re-exports the picker
// components, which would drag the whole calendar/popover stack into the eager
// initial bundle even though pickers only render in lazy routes.
import { provideHlmDatePickerConfig } from '@/shared/components/spartan/date-picker/src/lib/hlm-date-picker.token';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
    provideNativeDateAdapter(),
    provideHlmDatePickerConfig<Date>({
      autoCloseOnSelect: true,
      formatDate: formatPickerDate,
      formatInputDate: formatPickerDate,
      parseDate: parsePickerDate,
    }),
    provideEchartsCore({ echarts: loadEcharts }),
    {
      provide: APP_INITIALIZER,
      useFactory: (auth: AuthService) => () => auth.initialize(),
      deps: [AuthService],
      multi: true,
    },
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};

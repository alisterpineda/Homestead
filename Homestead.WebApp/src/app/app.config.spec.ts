import { createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpBackend, HttpXhrBackend } from '@angular/common/http';
import { appConfig } from './app.config';

describe('appConfig', () => {
  // Angular 22's default FetchBackend never emits upload progress, so the upload bar would sit at
  // 0% and jump to 100%. No HTTP spec would notice: they all run on HttpTestingController.
  it('should send requests through the XHR backend, which reports upload progress', () => {
    const injector = createEnvironmentInjector(
      appConfig.providers,
      TestBed.inject(EnvironmentInjector),
    );

    expect(injector.get(HttpBackend)).toBeInstanceOf(HttpXhrBackend);

    injector.destroy();
  });
});

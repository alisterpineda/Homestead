import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Component } from '@angular/core';
import { AdminSidebarComponent } from './admin-sidebar.component';

@Component({ template: '' })
class DummyComponent {}

describe('AdminSidebarComponent', () => {
  let fixture: ComponentFixture<AdminSidebarComponent>;
  let component: AdminSidebarComponent;
  let router: Router;

  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn(() => null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });

    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'admin', component: DummyComponent },
          { path: 'admin/users', component: DummyComponent },
          { path: 'admin/users/:id', component: DummyComponent },
          { path: 'admin/groups', component: DummyComponent },
          { path: 'admin/groups/:id', component: DummyComponent },
          { path: 'admin/jobs', component: DummyComponent },
        ]),
      ],
    });

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(AdminSidebarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  describe('isActive', () => {
    it('should return true for exact match on /admin', async () => {
      await router.navigateByUrl('/admin');
      expect(component.isActive('/admin')).toBe(true);
    });

    it('should return true for exact match on /admin/users', async () => {
      await router.navigateByUrl('/admin/users');
      expect(component.isActive('/admin/users')).toBe(true);
    });

    it('should return true for /admin/users when on a user detail sub-route', async () => {
      await router.navigateByUrl('/admin/users/user-123');
      expect(component.isActive('/admin/users')).toBe(true);
    });

    it('should return false for /admin when on /admin/users', async () => {
      await router.navigateByUrl('/admin/users');
      expect(component.isActive('/admin')).toBe(false);
    });

    it('should return false for /admin when on /admin/groups', async () => {
      await router.navigateByUrl('/admin/groups');
      expect(component.isActive('/admin')).toBe(false);
    });

    it('should return false for /admin when on /admin/jobs', async () => {
      await router.navigateByUrl('/admin/jobs');
      expect(component.isActive('/admin')).toBe(false);
    });

    it('should return false for non-matching paths', async () => {
      await router.navigateByUrl('/admin/groups');
      expect(component.isActive('/admin/users')).toBe(false);
    });

    it('should return true for exact match on /admin/groups', async () => {
      await router.navigateByUrl('/admin/groups');
      expect(component.isActive('/admin/groups')).toBe(true);
    });

    it('should return true for exact match on /admin/jobs', async () => {
      await router.navigateByUrl('/admin/jobs');
      expect(component.isActive('/admin/jobs')).toBe(true);
    });
  });
});

import {
  Component,
  ChangeDetectionStrategy,
  computed,
  inject,
  input,
} from '@angular/core';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';
import { ThemeService } from '@/shared/services/theme.service';
import type { NetWorthGranularity } from '@/api/generated/models/net-worth-granularity';
import type { NetWorthHistoryPoint } from '@/api/generated/models/net-worth-history-point';

@Component({
  selector: 'app-net-worth-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgxEchartsDirective],
  template: `
    <div
      echarts
      [options]="options()"
      [autoResize]="true"
      class="block h-[420px] w-full"
      role="img"
      [attr.aria-label]="ariaLabel()"
    ></div>
  `,
})
export class NetWorthChartComponent {
  private themeService = inject(ThemeService);

  readonly points = input.required<readonly NetWorthHistoryPoint[]>();
  readonly granularity = input.required<NetWorthGranularity>();
  readonly displayCurrencySymbol = input<string | null>(null);
  readonly xAxisMin = input<string | null>(null);
  readonly xAxisMax = input<string | null>(null);

  protected readonly ariaLabel = computed(() =>
    `Net worth over time, ${this.points().length} data points in ${this.displayCurrencySymbol() ?? 'display currency'}.`,
  );

  protected readonly options = computed<EChartsCoreOption>(() => {
    // Re-read CSS variables whenever resolved theme flips so the chart re-themes.
    const resolved = this.themeService.resolved();
    void resolved;
    const colors = readThemeColors();
    const symbol = this.displayCurrencySymbol();
    const granularity = this.granularity();

    const data = this.points().map(p => [p.asOf, p.value ?? null]);

    return {
      grid: { left: 8, right: 16, top: 16, bottom: 8, containLabel: true },
      tooltip: {
        trigger: 'axis',
        backgroundColor: colors.popover,
        borderColor: colors.border,
        textStyle: { color: colors.popoverFg },
        formatter: (params: unknown) => formatTooltip(params, symbol, granularity),
      },
      xAxis: {
        type: 'time',
        min: this.xAxisMin() ?? undefined,
        max: this.xAxisMax() ?? undefined,
        axisLine: { lineStyle: { color: colors.border } },
        axisLabel: { color: colors.mutedFg, hideOverlap: true },
        splitLine: { show: false },
      },
      yAxis: {
        type: 'value',
        scale: true,
        axisLine: { show: false },
        axisLabel: {
          color: colors.mutedFg,
          formatter: (value: number) => formatAxis(value, symbol),
        },
        splitLine: { lineStyle: { color: colors.border, type: 'dashed' } },
      },
      series: [
        {
          type: 'line',
          smooth: true,
          showSymbol: this.points().length <= 1,
          connectNulls: false,
          data,
          lineStyle: { color: colors.primary, width: 2 },
          itemStyle: { color: colors.primary },
          areaStyle: {
            color: colors.primary,
            opacity: 0.12,
          },
        },
      ],
    };
  });
}

interface ThemeColors {
  primary: string;
  mutedFg: string;
  border: string;
  popover: string;
  popoverFg: string;
}

function readThemeColors(): ThemeColors {
  const root = document.documentElement;
  const cs = getComputedStyle(root);
  const get = (name: string, fallback: string) => toRgb(cs.getPropertyValue(name).trim(), fallback);
  return {
    primary: get('--primary', '#2563eb'),
    mutedFg: get('--muted-foreground', '#64748b'),
    border: get('--border', '#e5e7eb'),
    popover: get('--popover', '#ffffff'),
    popoverFg: get('--popover-foreground', '#0f172a'),
  };
}

let probeCanvas: HTMLCanvasElement | undefined;

// ECharts paints oklch() correctly on first render but its emphasis-state code
// path lightens colors through an internal parser that only understands sRGB —
// hovering then resolves the line/area to transparent. Normalising to rgb()
// up-front via a 1×1 canvas keeps the highlight pipeline intact across themes.
function toRgb(value: string, fallback: string): string {
  if (!value) return fallback;
  try {
    probeCanvas ??= document.createElement('canvas');
    probeCanvas.width = probeCanvas.height = 1;
    const ctx = probeCanvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return fallback;
    ctx.clearRect(0, 0, 1, 1);
    ctx.fillStyle = value;
    ctx.fillRect(0, 0, 1, 1);
    const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
    return a === 255 ? `rgb(${r}, ${g}, ${b})` : `rgba(${r}, ${g}, ${b}, ${(a / 255).toFixed(3)})`;
  } catch {
    return fallback;
  }
}

function formatAxis(value: number, symbol: string | null): string {
  return new Intl.NumberFormat(undefined, {
    style: symbol ? 'currency' : 'decimal',
    currency: symbol ?? undefined,
    maximumFractionDigits: 0,
  }).format(value);
}

interface AxisTooltipParam {
  axisValue: string | number;
  data: [string, number | null];
  marker: string;
}

function formatTooltip(
  params: unknown,
  symbol: string | null,
  granularity: NetWorthGranularity,
): string {
  const arr = Array.isArray(params) ? (params as AxisTooltipParam[]) : [params as AxisTooltipParam];
  if (arr.length === 0) return '';
  const first = arr[0];
  const date = new Date(first.data[0]);
  const dateLabel = formatDateLabel(date, granularity);
  const value = first.data[1];
  const valueLabel = value === null
    ? '—'
    : new Intl.NumberFormat(undefined, {
        style: symbol ? 'currency' : 'decimal',
        currency: symbol ?? undefined,
        minimumFractionDigits: 0,
        maximumFractionDigits: 2,
      }).format(value);
  return `<div style="font-size:12px"><div style="margin-bottom:4px;opacity:.8">${dateLabel}</div>${first.marker}<strong>${valueLabel}</strong></div>`;
}

function formatDateLabel(d: Date, granularity: NetWorthGranularity): string {
  return granularity === 'Monthly'
    ? d.toLocaleDateString(undefined, { year: 'numeric', month: 'long' })
    : d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

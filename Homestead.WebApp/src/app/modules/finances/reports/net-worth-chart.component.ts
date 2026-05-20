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
import { formatNetWorthValue } from './format-net-worth';

const SERIES = {
  NetWorth: 'Net worth',
  Assets: 'Assets',
  Liabilities: 'Liabilities',
} as const;

@Component({
  selector: 'app-net-worth-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgxEchartsDirective],
  template: `
    <div
      echarts
      [options]="options()"
      [autoResize]="true"
      class="block h-[280px] w-full touch-none md:h-[420px]"
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
  readonly showBreakdown = input<boolean>(false);
  readonly masked = input<boolean>(false);

  protected readonly ariaLabel = computed(() =>
    `Net worth over time, ${this.points().length} data points in ${this.displayCurrencySymbol() ?? 'display currency'}.`,
  );

  // Re-read CSS variables only when resolved theme flips so the chart re-themes
  // without paying the canvas-probe cost on every points/range/breakdown change.
  private readonly themeColors = computed(() => {
    this.themeService.resolved();
    return readThemeColors();
  });

  protected readonly options = computed<EChartsCoreOption>(() => {
    const colors = this.themeColors();
    const symbol = this.displayCurrencySymbol();
    const granularity = this.granularity();
    const points = this.points();
    const showBreakdown = this.showBreakdown();
    const masked = this.masked();

    const lineSeries = {
      name: SERIES.NetWorth,
      type: 'line',
      smooth: true,
      showSymbol: points.length <= 1,
      connectNulls: false,
      data: points.map(p => [p.asOf, p.value ?? null]),
      lineStyle: { color: colors.primary, width: 2 },
      itemStyle: { color: colors.primary },
      areaStyle: { color: colors.primary, opacity: 0.12 },
      z: 3,
    };

    const breakdownConfigs: { name: string; field: 'assets' | 'liabilities'; color: string }[] = [
      { name: SERIES.Assets, field: 'assets', color: colors.assetBar },
      { name: SERIES.Liabilities, field: 'liabilities', color: colors.destructive },
    ];
    const breakdownSeries = breakdownConfigs.map(b => ({
      name: b.name,
      type: 'line',
      smooth: true,
      showSymbol: false,
      data: points.map(p => [p.asOf, p[b.field] ?? null]),
      lineStyle: { color: b.color, width: 1, opacity: 0.6 },
      itemStyle: { color: b.color },
      areaStyle: { color: b.color, opacity: 0.22 },
      z: 1,
    }));

    const series = showBreakdown ? [...breakdownSeries, lineSeries] : [lineSeries];

    return {
      grid: { left: 8, right: 16, top: 16, bottom: 8, containLabel: true },
      tooltip: {
        trigger: 'axis',
        axisPointer: { z: 1 },
        confine: true,
        backgroundColor: colors.popover,
        borderColor: colors.border,
        textStyle: { color: colors.popoverFg },
        formatter: (params: unknown) => formatTooltip(params, symbol, granularity, masked),
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
          formatter: (value: number) => formatAxis(value, symbol, masked),
        },
        splitLine: { lineStyle: { color: colors.border, type: 'dashed' } },
      },
      series,
    };
  });
}

interface ThemeColors {
  primary: string;
  destructive: string;
  assetBar: string;
  mutedFg: string;
  border: string;
  popover: string;
  popoverFg: string;
}

function readThemeColors(): ThemeColors {
  const root = document.documentElement;
  const cs = getComputedStyle(root);
  const get = (name: string, fallback: string) => toRgb(cs.getPropertyValue(name).trim(), fallback);
  // Asset bars use a fixed blue so they read as the visual dual of the destructive
  // red liability bars — `--primary` is intentionally neutral in this theme.
  const isDark = root.classList.contains('dark');
  return {
    primary: get('--primary', '#2563eb'),
    destructive: get('--destructive', '#dc2626'),
    assetBar: toRgb(isDark ? 'oklch(0.704 0.15 240)' : 'oklch(0.577 0.18 240)', isDark ? '#60a5fa' : '#2563eb'),
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

function formatAxis(value: number, symbol: string | null, masked: boolean): string {
  return formatNetWorthValue(value, symbol, masked, { minFraction: 0, maxFraction: 0 });
}

interface AxisTooltipParam {
  axisValue: string | number;
  data: [string, number | null];
  marker: string;
  seriesName?: string;
}

function formatTooltip(
  params: unknown,
  symbol: string | null,
  granularity: NetWorthGranularity,
  masked: boolean,
): string {
  const arr = Array.isArray(params) ? (params as AxisTooltipParam[]) : [params as AxisTooltipParam];
  if (arr.length === 0) return '';
  const first = arr[0];
  const date = new Date(first.data[0]);
  const dateLabel = formatDateLabel(date, granularity);
  const fmt = (v: number | null) =>
    v === null
      ? '—'
      : formatNetWorthValue(v, symbol, masked, { minFraction: 0, maxFraction: 2 });

  const head = `<div style="margin-bottom:4px;opacity:.8">${dateLabel}</div>`;

  if (arr.length === 1) {
    return `<div style="font-size:12px">${head}${first.marker}<strong>${fmt(first.data[1])}</strong></div>`;
  }

  // Liabilities are beancount-signed (negative); render as a negated magnitude
  // so the tooltip reads like "−$50,000.00" rather than the raw negative.
  const negate = (v: number | null) =>
    v === null ? '—' : v === 0 ? fmt(0) : `−${fmt(Math.abs(v))}`;
  const rows: { name: string; label: string; format: (v: number | null) => string }[] = [
    { name: SERIES.NetWorth, label: 'Net worth', format: fmt },
    { name: SERIES.Assets, label: 'Assets', format: fmt },
    { name: SERIES.Liabilities, label: 'Liabilities', format: negate },
  ];
  const byName = new Map<string, AxisTooltipParam>();
  for (const p of arr) {
    if (p.seriesName) byName.set(p.seriesName, p);
  }
  const lines = rows
    .map(r => {
      const p = byName.get(r.name);
      return p ? `<div>${p.marker}${r.label}&nbsp;<strong>${r.format(p.data[1])}</strong></div>` : '';
    })
    .filter(Boolean)
    .join('');
  return `<div style="font-size:12px">${head}${lines}</div>`;
}

function formatDateLabel(d: Date, granularity: NetWorthGranularity): string {
  return granularity === 'Monthly'
    ? d.toLocaleDateString(undefined, { year: 'numeric', month: 'long' })
    : d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

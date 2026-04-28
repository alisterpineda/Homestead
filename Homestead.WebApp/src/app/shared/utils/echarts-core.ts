// Lazy-loaded ECharts core so the library only ships when the user lands on a
// chart-using page. Keeps the initial JS bundle small. ngx-echarts awaits this
// thunk on first directive instantiation.
export async function loadEcharts(): Promise<typeof import('echarts/core')> {
  const [echarts, charts, components, renderers] = await Promise.all([
    import('echarts/core'),
    import('echarts/charts'),
    import('echarts/components'),
    import('echarts/renderers'),
  ]);
  echarts.use([
    charts.LineChart,
    components.GridComponent,
    components.TooltipComponent,
    renderers.CanvasRenderer,
  ]);
  return echarts;
}

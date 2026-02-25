export function formatBytes(bytes: number, decimals = 1): string {
  if (bytes === 0) return '0 B';

  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  const size = sizes[i] ?? 'TB';

  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(decimals))} ${size}`;
}

'use client';

import { Group, Text } from '@mantine/core';

export type LineSeries = { label: string; color: string; values: number[]; strong?: boolean };

const WIDTH = 640;
const HEIGHT = 220;
const PAD = { left: 44, right: 16, top: 16, bottom: 32 };

/** Line chart drawn as SVG: one line per series over the given months, with a value scale starting at zero. */
export function LineChart({ labels, series, label, labelEvery = 1 }: { labels: string[]; series: LineSeries[]; label: string; labelEvery?: number }) {
  const max = Math.max(1, ...series.flatMap(s => s.values));
  const innerWidth = WIDTH - PAD.left - PAD.right;
  const innerHeight = HEIGHT - PAD.top - PAD.bottom;
  const x = (index: number) => PAD.left + (labels.length <= 1 ? innerWidth / 2 : (index * innerWidth) / (labels.length - 1));
  const y = (value: number) => PAD.top + (1 - value / max) * innerHeight;
  const ticks = [0, max / 2, max];

  return <div>
    <svg role="img" aria-label={label} viewBox={`0 0 ${WIDTH} ${HEIGHT}`} width="100%" style={{ display: 'block', width: '100%', height: 'auto' }}>
      {ticks.map(tick => <g key={tick}>
        <line x1={PAD.left} x2={WIDTH - PAD.right} y1={y(tick)} y2={y(tick)} stroke="#e5e7eb" />
        <text x={PAD.left - 6} y={y(tick) + 4} fontSize="11" textAnchor="end" fill="#6b7280">{Number.isInteger(tick) ? tick : tick.toFixed(1)}</text>
      </g>)}
      {labels.map((text, index) => index % labelEvery === 0 ? <text key={text + index} x={x(index)} y={HEIGHT - 10} fontSize="11" textAnchor="middle" fill="#6b7280">{text}</text> : null)}
      {series.map(line => <g key={line.label}>
        <polyline fill="none" stroke={line.color} strokeWidth={line.strong ? 3 : 2} strokeLinejoin="round" strokeLinecap="round"
          points={line.values.map((value, index) => `${x(index)},${y(value)}`).join(' ')} />
        {line.values.map((value, index) => <circle key={index} cx={x(index)} cy={y(value)} r={line.strong ? 4 : 3} fill={line.color} />)}
      </g>)}
    </svg>
    <Group gap="md" mt={6} wrap="wrap">
      {series.map(line => <Group key={line.label} gap={6} wrap="nowrap">
        <span style={{ width: 14, height: 3, borderRadius: 2, background: line.color, display: 'inline-block' }} aria-hidden="true" />
        <Text size="xs">{line.label}</Text>
      </Group>)}
    </Group>
  </div>;
}

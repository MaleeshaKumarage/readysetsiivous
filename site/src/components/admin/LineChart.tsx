'use client';

import { useState } from 'react';
import { AgCharts } from 'ag-charts-react';
import { Group, Text, UnstyledButton } from '@mantine/core';
import '@/lib/agCharts';
import type { AgCartesianChartOptions } from 'ag-charts-community';

export type LineSeries = { label: string; color: string; values: number[]; strong?: boolean };

const DASHES: number[][] = [[], [6, 4], [2, 3]];
const SHAPES = ['circle', 'diamond', 'square'] as const;

/** Line chart on AG Charts: one line per series over the given months, with a value scale starting at zero. Click a legend entry to hide or show its line. */
export function LineChart({ labels, series, label }: { labels: string[]; series: LineSeries[]; label: string; labelEvery?: number }) {
  const [hidden, setHidden] = useState<Set<string>>(() => new Set());
  const toggle = (name: string) => setHidden(current => {
    const next = new Set(current);
    if (next.has(name)) next.delete(name); else next.add(name);
    return next;
  });

  const data = labels.map((text, index) => {
    const row: Record<string, string | number> = { label: text };
    series.forEach((line, lineIndex) => { row[`line${lineIndex}`] = line.values[index] ?? 0; });
    return row;
  });
  const options: AgCartesianChartOptions = {
    data,
    height: 240,
    background: { visible: false },
    series: series.map((line, lineIndex) => ({
      type: 'line',
      xKey: 'label',
      yKey: `line${lineIndex}`,
      yName: line.label,
      visible: !hidden.has(line.label),
      stroke: line.color,
      strokeWidth: line.strong ? 3 : 2,
      // Dash pattern and marker shape differ per series, so lines stay distinguishable without colour.
      lineDash: DASHES[lineIndex % DASHES.length],
      marker: { enabled: true, shape: SHAPES[lineIndex % SHAPES.length], size: line.strong ? 8 : 6, fill: line.color, stroke: line.color },
      tooltip: { renderer: ({ datum, yKey }) => ({ title: `${line.label} · ${String(datum.label)}`, content: `${Number(datum[yKey]).toLocaleString('en-GB')}` }) },
    })),
    axes: {
      x: { type: 'category', position: 'bottom' },
      y: { type: 'number', position: 'left', min: 0, nice: true },
    },
    // The legend is a text control below the chart, so it is keyboard-accessible and readable by tests.
    legend: { enabled: false },
    tooltip: { enabled: true },
  };

  return <div>
    <div role="img" aria-label={label} style={{ width: '100%' }}>
      <AgCharts options={options} />
    </div>
    <Group gap="md" mt={6} wrap="wrap" role="group" aria-label={`${label} legend`}>
      {series.map((line, lineIndex) => {
        const off = hidden.has(line.label);
        return <UnstyledButton key={line.label} onClick={() => toggle(line.label)} aria-pressed={!off}
          style={{ display: 'flex', alignItems: 'center', gap: 6, opacity: off ? 0.45 : 1, minHeight: 32 }}>
          <span style={{ width: 14, height: 3, borderRadius: 2, background: line.color, display: 'inline-block', borderTop: DASHES[lineIndex % DASHES.length].length ? `2px dashed ${line.color}` : undefined }} aria-hidden="true" />
          <Text size="xs" component="span" td={off ? 'line-through' : undefined}>{line.label}</Text>
        </UnstyledButton>;
      })}
    </Group>
  </div>;
}

'use client';

import { useMemo, type ReactNode } from 'react';
import { AgGridReact } from 'ag-grid-react';
import { AllCommunityModule, ModuleRegistry, themeQuartz, type ColDef } from 'ag-grid-community';

ModuleRegistry.registerModules([AllCommunityModule]);

// Design-system table theme: navy ink on white, brand-gold header (the same tokens as Tailwind accent/brand).
const dsTheme = themeQuartz.withParams({
  fontFamily: 'Inter, system-ui, sans-serif',
  fontSize: 14,
  accentColor: '#0a1741',
  foregroundColor: '#0a1741',
  headerBackgroundColor: '#c9a340',
  headerTextColor: '#0a1741',
  headerFontWeight: 700,
  rowHoverColor: '#f3f5fa',
  borderColor: '#e4e8f2',
  borderRadius: 12,
  wrapperBorderRadius: 12,
});

export interface DsGridProps<T> {
  rows: T[];
  columns: ColDef<T>[];
  /** Accessible name for the grid region. */
  label: string;
  /** Row id, so updates keep the same rows. */
  getRowId?: (row: T) => string;
  height?: number | string;
  /** Rows that span the full width (group headings). Return true for those rows. */
  isFullWidthRow?: (row: T) => boolean;
  /** What a full-width row shows. */
  fullWidthRow?: (row: T) => ReactNode;
  /** Called when a row is clicked (full-width rows included; check the row kind in the handler). */
  onRowClicked?: (row: T) => void;
}

/** The design-system table: AG Grid with the navy and gold theme. Use this for every table. */
export function DsGrid<T>({ rows, columns, label, getRowId, height = 'auto', isFullWidthRow, fullWidthRow, onRowClicked }: DsGridProps<T>) {
  const defaultColDef = useMemo<ColDef<T>>(() => ({ resizable: true, sortable: true, flex: 1, minWidth: 110 }), []);
  return (
    <div role="region" aria-label={label} style={{ height, width: '100%' }}>
      <AgGridReact<T>
        theme={dsTheme}
        rowData={rows}
        columnDefs={columns}
        defaultColDef={defaultColDef}
        getRowId={getRowId ? (params) => getRowId(params.data) : undefined}
        domLayout={height === 'auto' ? 'autoHeight' : 'normal'}
        suppressCellFocus
        suppressColumnVirtualisation
        onRowClicked={onRowClicked ? (event) => { if (event.data) onRowClicked(event.data); } : undefined}
        isFullWidthRow={isFullWidthRow ? (params) => !!params.rowNode.data && isFullWidthRow(params.rowNode.data) : undefined}
        fullWidthCellRenderer={fullWidthRow ? FullWidthRow : undefined}
        fullWidthCellRendererParams={fullWidthRow ? { render: fullWidthRow } : undefined}
      />
    </div>
  );
}

// Renders a full-width row through the render prop passed in fullWidthCellRendererParams.
function FullWidthRow(params: { data?: unknown; render?: (row: unknown) => ReactNode }) {
  return params.data && params.render ? <div className="px-3 py-2 font-bold text-accent-950">{params.render(params.data)}</div> : null;
}

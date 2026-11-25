import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AgGridReact } from 'ag-grid-react';
import type { CellKeyDownEvent, CellValueChangedEvent, ColDef, GetRowIdParams, GridApi, GridReadyEvent, IDatasource } from 'ag-grid-community';
import { postJson, ApiError } from '../api/client';
import type { ChangeBatchResult, ColumnModel, Row, TableModel, TableRowsResponse } from '../api/types';
import { agFilterFor, toApiFilterModel } from '../utils/filters';
import { LookupPopup } from './LookupPopup';
import { RelationPanel } from './RelationPanel';

interface Props {
  connectionId: string;
  table: TableModel;
  active: boolean;
  onOpenScript: (title: string, text: string) => void;
}

interface PendingUpdate {
  keys: Row;
  values: Row;
  rowVersion: string | null;
}

interface LookupState {
  rowKey: string;
  column: string;
  value: unknown;
}

export function TableTab({ connectionId, table, active, onOpenScript }: Props) {
  const apiRef = useRef<GridApi | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const [quickFilter, setQuickFilter] = useState('');
  const [showFind, setShowFind] = useState(false);
  const [findDraft, setFindDraft] = useState('');
  const [lookup, setLookup] = useState<LookupState | null>(null);
  const [pending, setPending] = useState<Map<string, PendingUpdate>>(new Map());
  const [inserts, setInserts] = useState<Row[]>([]);
  const [showInsert, setShowInsert] = useState(false);
  const [selected, setSelected] = useState<Row[]>([]);
  const [relationKeys, setRelationKeys] = useState<Row[] | null>(null);
  const [total, setTotal] = useState<number | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[]>([]);

  const primaryKey = table.primaryKey;
  const rowVersionColumn = table.columns.find((c) => c.isRowVersion)?.name ?? null;
  const writable = !table.isView && primaryKey.length > 0;

  const pkOf = useCallback((row: Row): Row => Object.fromEntries(primaryKey.map((k) => [k, row[k]])), [primaryKey]);
  const rowKeyOf = useCallback((row: Row): string => JSON.stringify(pkOf(row)), [pkOf]);
  const rowVersionOf = (row: Row): string | null => (rowVersionColumn ? (row[rowVersionColumn] as string) : null);

  const columnDefs = useMemo<ColDef[]>(
    () =>
      table.columns.map((c) => ({
        field: c.name,
        headerName: c.name,
        headerTooltip: c.sqlType,
        editable: writable && isEditable(c, primaryKey),
        filter: agFilterFor(c),
        sortable: true,
        resizable: true,
        minWidth: 90,
      })),
    [table, writable, primaryKey],
  );

  const datasource = useMemo<IDatasource>(
    () => ({
      async getRows(params) {
        const { startRow, endRow, sortModel, filterModel } = params;
        try {
          const response = await postJson<TableRowsResponse>(`/api/connections/${connectionId}/tables/${table.schema}/${table.name}/rows`, {
            startRow,
            endRow,
            sortModel: sortModel.map((s) => ({ colId: s.colId, sort: s.sort })),
            filterModel: toApiFilterModel(filterModel),
            quickFilter: quickFilter || null,
            includeCount: true,
          });
          setTotal(response.lastRow);
          params.successCallback(response.rows, response.lastRow ?? undefined);
        } catch (err) {
          setErrors([errorText(err)]);
          params.failCallback();
        }
      },
    }),
    [connectionId, table, quickFilter],
  );

  const getRowId = useCallback((p: GetRowIdParams) => rowKeyOf(p.data as Row), [rowKeyOf]);

  // The React wrapper does not re-apply a changed datasource to the infinite model, so do it explicitly.
  useEffect(() => {
    apiRef.current?.setGridOption('datasource', datasource);
  }, [datasource]);

  function onGridReady(e: GridReadyEvent) {
    apiRef.current = e.api;
  }

  function onCellValueChanged(e: CellValueChangedEvent) {
    const field = e.colDef.field;
    if (!field || e.data == null) return;
    const row = e.data as Row;
    const key = rowKeyOf(row);
    setPending((prev) => {
      const next = new Map(prev);
      const entry = next.get(key) ?? { keys: pkOf(row), values: {}, rowVersion: rowVersionOf(row) };
      entry.values = { ...entry.values, [field]: e.newValue };
      next.set(key, entry);
      return next;
    });
  }

  // Ctrl+L on a cell: opens the lookup popup for that cell's value.
  function onCellKeyDown(e: CellKeyDownEvent) {
    const event = e.event as KeyboardEvent | null;
    if (!event || !event.ctrlKey || event.key.toLowerCase() !== 'l' || !e.colDef.field || !e.data) return;
    event.preventDefault();
    const row = e.data as Row;
    setLookup({ rowKey: rowKeyOf(row), column: e.colDef.field, value: row[e.colDef.field] });
  }

  function applyLookupValue(value: unknown) {
    if (!lookup) return;
    const node = apiRef.current?.getRowNode(lookup.rowKey);
    node?.setDataValue(lookup.column, value);
    setLookup(null);
  }

  // Ctrl+F anywhere in the tab opens the quick filter instead of the browser's find bar.
  // A capture listener runs before AG Grid's own key handling, which would otherwise swallow the event.
  useEffect(() => {
    const root = rootRef.current;
    if (!root) return;
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'f') {
        e.preventDefault();
        e.stopPropagation();
        setFindDraft(quickFilter);
        setShowFind(true);
      }
    };
    root.addEventListener('keydown', onKey, true);
    return () => root.removeEventListener('keydown', onKey, true);
  }, [quickFilter]);

  function applyFind() {
    setQuickFilter(findDraft.trim());
    setShowFind(false);
  }

  async function saveChanges() {
    setMessage(null);
    setErrors([]);
    try {
      const result = await postJson<ChangeBatchResult>(`/api/connections/${connectionId}/tables/${table.schema}/${table.name}/changes`, {
        updates: [...pending.values()].map((u) => ({ keys: u.keys, values: u.values, rowVersion: u.rowVersion })),
        inserts: inserts.map((values) => ({ values })),
        deletes: [],
      });
      setPending(new Map());
      setInserts([]);
      setMessage(`Saved: ${result.updated.length} updated, ${result.inserted.length} inserted.`);
      apiRef.current?.refreshInfiniteCache();
    } catch (err) {
      setErrors(conflictMessages(err));
    }
  }

  async function deleteSelected() {
    if (selected.length === 0) return;
    if (!window.confirm(`Delete ${selected.length} row(s) from ${table.schema}.${table.name}?`)) return;
    setMessage(null);
    setErrors([]);
    try {
      const result = await postJson<ChangeBatchResult>(`/api/connections/${connectionId}/tables/${table.schema}/${table.name}/changes`, {
        updates: [],
        inserts: [],
        deletes: selected.map((r) => ({ keys: pkOf(r), rowVersion: rowVersionOf(r) })),
      });
      setMessage(`Deleted ${result.deleted} row(s).`);
      apiRef.current?.refreshInfiniteCache();
    } catch (err) {
      setErrors(conflictMessages(err));
    }
  }

  const insertableColumns = table.columns.filter((c) => isInsertable(c));
  const dirty = pending.size + inserts.length;

  return (
    <div ref={rootRef} className="table-tab" hidden={!active}>
      <div className="toolbar">
        <strong>
          {table.schema}.{table.name}
        </strong>
        {table.isView && <span className="badge">view · read-only</span>}
        {!primaryKey.length && !table.isView && <span className="badge warn">no primary key · edits disabled</span>}
        <span className="grow" />
        {quickFilter && (
          <span className="chip">
            find: {quickFilter}
            <button className="small" onClick={() => setQuickFilter('')}>
              ×
            </button>
          </span>
        )}
        <button className="small" onClick={() => { setFindDraft(quickFilter); setShowFind(true); }}>
          Find (Ctrl+F)
        </button>
        <button className="small" disabled={!writable} onClick={() => setShowInsert(true)}>
          Add row
        </button>
        <button className="small primary" disabled={dirty === 0} onClick={saveChanges}>
          Save {dirty > 0 ? `(${dirty})` : ''}
        </button>
        <button className="small danger" disabled={!writable || selected.length === 0} onClick={deleteSelected}>
          Delete selected
        </button>
        <button className="small" disabled={!writable || selected.length === 0} onClick={() => setRelationKeys(selected.map(pkOf))}>
          Relations ({selected.length})
        </button>
      </div>

      {showFind && (
        <div className="find-bar">
          <input
            autoFocus
            placeholder="Text to find in any column (Enter to apply)"
            value={findDraft}
            onChange={(e) => setFindDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') applyFind();
              if (e.key === 'Escape') setShowFind(false);
            }}
          />
          <button className="small primary" onClick={applyFind}>
            Apply
          </button>
          <button className="small" onClick={() => setShowFind(false)}>
            Cancel
          </button>
        </div>
      )}

      {message && <div className="ok">{message}</div>}
      {errors.map((e, i) => (
        <div key={i} className="error">
          {e}
        </div>
      ))}

      <div className="grid-host">
        <AgGridReact
          columnDefs={columnDefs}
          rowModelType="infinite"
          datasource={datasource}
          cacheBlockSize={200}
          maxBlocksInCache={20}
          infiniteInitialRowCount={200}
          getRowId={getRowId}
          rowSelection={{ mode: 'multiRow', checkboxes: true }}
          defaultColDef={{ floatingFilter: true }}
          onGridReady={onGridReady}
          onCellValueChanged={onCellValueChanged}
          onCellKeyDown={onCellKeyDown}
          onSelectionChanged={(e) => setSelected(e.api.getSelectedRows() as Row[])}
        />
      </div>

      <div className="status-bar">
        {total === null ? 'Loading…' : `${total.toLocaleString()} row(s)`}
        {quickFilter && ` · filtered by "${quickFilter}"`}
        {' · Ctrl+L on a cell opens its lookup'}
      </div>

        {relationKeys && (
        <RelationPanel
          connectionId={connectionId}
          table={table}
          keys={relationKeys}
          onClose={() => setRelationKeys(null)}
          onOpenScript={onOpenScript}
        />
      )}

      {lookup && (
        <LookupPopup
          connectionId={connectionId}
          table={table}
          column={lookup.column}
          value={lookup.value}
          onPick={applyLookupValue}
          onClose={() => setLookup(null)}
        />
      )}

      {showInsert && (
        <InsertDialog
          columns={insertableColumns}
          onAdd={(row) => {
            setInserts((prev) => [...prev, row]);
            setShowInsert(false);
          }}
          onCancel={() => setShowInsert(false)}
        />
      )}
    </div>
  );
}

function isEditable(c: ColumnModel, primaryKey: string[]): boolean {
  return !c.isIdentity && !c.isComputed && !c.isRowVersion && !primaryKey.includes(c.name);
}

function isInsertable(c: ColumnModel): boolean {
  return !c.isIdentity && !c.isComputed && !c.isRowVersion;
}

function errorText(err: unknown): string {
  return err instanceof Error ? err.message : 'Request failed.';
}

// A 409 from the changes endpoint lists per-operation errors; show each one.
function conflictMessages(err: unknown): string[] {
  if (err instanceof ApiError && err.status === 409) {
    const body = err.body as { errors?: { group: string; index: number; message: string }[] } | undefined;
    const items = body?.errors ?? [];
    return [
      'The batch was rolled back, so no change was saved.',
      ...items.map((i) => `${i.group} #${i.index + 1}: ${i.message}`),
    ];
  }
  return [errorText(err)];
}

function InsertDialog({
  columns,
  onAdd,
  onCancel,
}: {
  columns: ColumnModel[];
  onAdd: (row: Row) => void;
  onCancel: () => void;
}) {
  const [values, setValues] = useState<Record<string, string>>({});

  function submit(e: React.FormEvent) {
    e.preventDefault();
    const row: Row = {};
    for (const c of columns) {
      const raw = values[c.name];
      if (raw === undefined || raw === '') {
        if (c.isNullable) row[c.name] = null;
        continue;
      }
      row[c.name] = raw;
    }
    onAdd(row);
  }

  return (
    <div className="modal-backdrop">
      <form className="modal" onSubmit={submit}>
        <h3>Add row</h3>
        {columns.map((c) => (
          <label key={c.name}>
            {c.name} <span className="hint">{c.sqlType}{c.isNullable ? '' : ' · required'}</span>
            <input value={values[c.name] ?? ''} onChange={(e) => setValues({ ...values, [c.name]: e.target.value })} />
          </label>
        ))}
        <div className="actions">
          <button type="submit" className="primary">
            Add to pending changes
          </button>
          <button type="button" onClick={onCancel}>
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
}

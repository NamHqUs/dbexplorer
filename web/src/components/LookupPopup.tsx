import { useEffect, useState } from 'react';
import { postJson } from '../api/client';
import type { LookupResponse, Row, TableModel } from '../api/types';

interface Props {
  connectionId: string;
  table: TableModel;
  column: string;
  value: unknown;
  onPick: (value: unknown) => void;
  onClose: () => void;
}

// Ctrl+L. For a foreign-key column it lists the referenced rows; for other columns it lists distinct values with counts.
export function LookupPopup({ connectionId, table, column, value, onPick, onClose }: Props) {
  const [search, setSearch] = useState('');
  const [result, setResult] = useState<LookupResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    const handle = window.setTimeout(async () => {
      try {
        const response = await postJson<LookupResponse>(`/api/connections/${connectionId}/tables/${table.schema}/${table.name}/lookup`, {
          column,
          value: value ?? null,
          search: search.trim() || null,
          top: 100,
        });
        if (!cancelled) setResult(response);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Lookup failed.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    }, 250);
    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [connectionId, table, column, value, search]);

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal lookup" onClick={(e) => e.stopPropagation()}>
        <h3>
          Lookup: {column}
          {result?.mode === 'foreignKey' && <span className="hint"> → {result.targetTable}.{result.targetColumn}</span>}
        </h3>
        <input
          autoFocus
          placeholder="Search"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => e.key === 'Escape' && onClose()}
        />
        {loading && <div className="hint">Loading…</div>}
        {error && <div className="error">{error}</div>}
        {result?.mode === 'foreignKey' && result.rows && <ReferencedRows result={result} onPick={onPick} />}
        {result?.mode === 'distinct' && result.values && (
          <table className="lookup-table">
            <thead>
              <tr>
                <th>Value</th>
                <th>Rows</th>
              </tr>
            </thead>
            <tbody>
              {result.values.map((v, i) => (
                <tr key={i} onClick={() => onPick(v.value)}>
                  <td>{String(v.value ?? 'NULL')}</td>
                  <td>{v.count.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <div className="actions">
          <button onClick={onClose}>Close</button>
        </div>
      </div>
    </div>
  );
}

function ReferencedRows({ result, onPick }: { result: LookupResponse; onPick: (value: unknown) => void }) {
  const rows = result.rows ?? [];
  if (rows.length === 0) return <div className="hint">No matching rows.</div>;
  const columns = Object.keys(rows[0]);
  return (
    <table className="lookup-table">
      <thead>
        <tr>
          {columns.map((c) => (
            <th key={c}>{c}</th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row: Row, i) => (
          <tr key={i} onClick={() => onPick(row[result.targetColumn ?? ''])}>
            {columns.map((c) => (
              <td key={c}>{String(row[c] ?? 'NULL')}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

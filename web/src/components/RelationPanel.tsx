import { useEffect, useMemo, useState } from 'react';
import { postJson } from '../api/client';
import type { CascadeScriptResponse, RelationGraph, Row, TableModel } from '../api/types';

interface Props {
  connectionId: string;
  table: TableModel;
  keys: Row[];
  onClose: () => void;
  onOpenScript: (title: string, text: string) => void;
}

// Shows every row related to the selected rows, and generates cascade scripts for them.
export function RelationPanel({ connectionId, table, keys, onClose, onOpenScript }: Props) {
  const [maxDepth, setMaxDepth] = useState(2);
  const [graph, setGraph] = useState<RelationGraph | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const request = useMemo(
    () => ({ schema: table.schema, table: table.name, keys, maxDepth, maxRowsPerEdge: 500 }),
    [table, keys, maxDepth],
  );

  useEffect(() => {
    let cancelled = false;
    setBusy(true);
    postJson<RelationGraph>(`/api/connections/${connectionId}/relations`, request)
      .then((g) => !cancelled && (setGraph(g), setError(null)))
      .catch((err) => !cancelled && setError(err instanceof Error ? err.message : 'Could not load relations.'))
      .finally(() => !cancelled && setBusy(false));
    return () => {
      cancelled = true;
    };
  }, [connectionId, request]);

  const grouped = useMemo(() => {
    const map = new Map<string, number>();
    for (const n of graph?.nodes ?? []) map.set(`${n.schema}.${n.table}`, (map.get(`${n.schema}.${n.table}`) ?? 0) + 1);
    return [...map.entries()];
  }, [graph]);

  async function script(mode: 'delete' | 'insert') {
    setBusy(true);
    try {
      const result = await postJson<CascadeScriptResponse>(`/api/connections/${connectionId}/scripts/cascade`, { relation: request, mode });
      const title = `${mode.toUpperCase()} script · ${table.schema}.${table.name}`;
      const text = [...result.warnings.map((w) => `-- WARNING: ${w}`), ...result.statements].join('\n');
      onOpenScript(title, text);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not generate the script.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="relation-panel">
      <div className="toolbar">
        <strong>Related rows</strong>
        <span className="hint">
          {keys.length} selected from {table.schema}.{table.name}
        </span>
        <span className="grow" />
        <label className="inline">
          Depth
          <select value={maxDepth} onChange={(e) => setMaxDepth(Number(e.target.value))}>
            {[1, 2, 3, 4].map((d) => (
              <option key={d} value={d}>
                {d}
              </option>
            ))}
          </select>
        </label>
        <button className="small" disabled={busy} onClick={() => script('delete')}>
          DELETE script
        </button>
        <button className="small" disabled={busy} onClick={() => script('insert')}>
          INSERT script
        </button>
        <button className="small" onClick={onClose}>
          Close
        </button>
      </div>
      {error && <div className="error">{error}</div>}
      {graph?.warnings.map((w, i) => (
        <div key={i} className="warn">
          {w}
        </div>
      ))}
      {busy && !graph && <div className="hint">Loading relations…</div>}
      {graph && (
        <div className="relation-body">
          <div className="summary">
            {grouped.map(([name, count]) => (
              <span key={name} className="chip">
                {name}: {count}
              </span>
            ))}
            {grouped.length === 0 && <span className="hint">No related rows found.</span>}
          </div>
          <ul className="relation-nodes">
            {graph.nodes.map((n) => (
              <li key={n.id} style={{ paddingLeft: `${n.depth * 16}px` }}>
                <span className="hint">depth {n.depth}</span> <strong>{n.schema}.{n.table}</strong>{' '}
                <code>{previewRow(n.row)}</code>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function previewRow(row: Row): string {
  return Object.entries(row)
    .slice(0, 5)
    .map(([k, v]) => `${k}=${String(v ?? 'NULL')}`)
    .join(', ');
}

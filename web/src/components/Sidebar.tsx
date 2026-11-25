import { useState } from 'react';
import { api, postJson } from '../api/client';
import type { ConnectionDto, ConnectionInput, DatabaseSchema, RoutineModel, TableModel, TestConnectionResult } from '../api/types';
import { ConnectionForm } from './ConnectionForm';

interface Props {
  connections: ConnectionDto[];
  selectedId: string | null;
  schema: DatabaseSchema | null;
  schemaLoading: boolean;
  onSelectConnection: (id: string) => void;
  onConnectionsChanged: () => Promise<void>;
  onRefreshSchema: () => void;
  onOpenTable: (table: TableModel) => void;
  onOpenRoutine: (routine: RoutineModel) => void;
  onOpenSql: () => void;
}

type Editing = { mode: 'new' } | { mode: 'edit'; connection: ConnectionDto } | null;

export function Sidebar(props: Props) {
  const { connections, selectedId, schema, schemaLoading } = props;
  const [editing, setEditing] = useState<Editing>(null);
  const [testResult, setTestResult] = useState<{ id: string; result: TestConnectionResult } | null>(null);
  const [filter, setFilter] = useState('');
  const [error, setError] = useState<string | null>(null);

  const needle = filter.trim().toLowerCase();
  const matches = (name: string) => needle === '' || name.toLowerCase().includes(needle);

  const tables = (schema?.tables ?? []).filter((t) => !t.isView && matches(`${t.schema}.${t.name}`));
  const views = (schema?.tables ?? []).filter((t) => t.isView && matches(`${t.schema}.${t.name}`));
  const procedures = (schema?.procedures ?? []).filter((r) => matches(`${r.schema}.${r.name}`));
  const functions = (schema?.functions ?? []).filter((r) => matches(`${r.schema}.${r.name}`));

  async function save(input: ConnectionInput) {
    if (editing?.mode === 'edit') {
      await api(`/api/connections/${editing.connection.id}`, { method: 'PUT', body: JSON.stringify(input) });
    } else {
      const created = await postJson<ConnectionDto>('/api/connections', input);
      props.onSelectConnection(created.id);
    }
    setEditing(null);
    await props.onConnectionsChanged();
  }

  async function remove(c: ConnectionDto) {
    if (!window.confirm(`Delete connection "${c.name}"?`)) return;
    try {
      await api<void>(`/api/connections/${c.id}`, { method: 'DELETE' });
      await props.onConnectionsChanged();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    }
  }

  async function test(c: ConnectionDto) {
    const result = await postJson<TestConnectionResult>(`/api/connections/${c.id}/test`, {});
    setTestResult({ id: c.id, result });
  }

  return (
    <aside className="sidebar">
      <section>
        <div className="section-title">
          Connections
          <button className="small" onClick={() => setEditing({ mode: 'new' })}>
            + New
          </button>
        </div>
        {editing && (
          <ConnectionForm
            initial={editing.mode === 'edit' ? editing.connection : null}
            onSave={save}
            onCancel={() => setEditing(null)}
          />
        )}
        <ul className="connection-list">
          {connections.map((c) => (
            <li key={c.id} className={c.id === selectedId ? 'selected' : ''}>
              <button className="link-button" onClick={() => props.onSelectConnection(c.id)}>
                {c.name}
                {c.readOnly && <span className="badge">read-only</span>}
              </button>
              <div className="conn-actions">
                <button className="small" onClick={() => setEditing({ mode: 'edit', connection: c })}>
                  Edit
                </button>
                <button className="small" onClick={() => test(c)}>
                  Test
                </button>
                <button className="small danger" onClick={() => remove(c)}>
                  Delete
                </button>
              </div>
              {testResult?.id === c.id && (
                <div className={testResult.result.success ? 'ok' : 'error'}>
                  {testResult.result.success
                    ? `Connected in ${testResult.result.elapsedMs} ms`
                    : testResult.result.error}
                </div>
              )}
            </li>
          ))}
        </ul>
        {error && <div className="error">{error}</div>}
      </section>

      <section className="schema">
        <div className="section-title">
          Schema
          <span>
            <button className="small" disabled={!selectedId} onClick={props.onRefreshSchema}>
              Refresh
            </button>
            <button className="small" disabled={!selectedId} onClick={props.onOpenSql}>
              SQL
            </button>
          </span>
        </div>
        {!selectedId && <div className="hint">Select a connection to browse its schema.</div>}
        {selectedId && (
          <input className="search" placeholder="Search objects" value={filter} onChange={(e) => setFilter(e.target.value)} />
        )}
        {schemaLoading && <div className="hint">Loading schema…</div>}
        {schema && (
          <>
            <Group title={`Tables (${tables.length})`}>
              {tables.map((t) => (
                <button key={`${t.schema}.${t.name}`} className="tree-item" onClick={() => props.onOpenTable(t)}>
                  {t.schema}.{t.name}
                </button>
              ))}
            </Group>
            <Group title={`Views (${views.length})`}>
              {views.map((t) => (
                <button key={`${t.schema}.${t.name}`} className="tree-item" onClick={() => props.onOpenTable(t)}>
                  {t.schema}.{t.name}
                </button>
              ))}
            </Group>
            <Group title={`Procedures (${procedures.length})`}>
              {procedures.map((r) => (
                <button key={`${r.schema}.${r.name}`} className="tree-item" onClick={() => props.onOpenRoutine(r)}>
                  {r.schema}.{r.name}
                </button>
              ))}
            </Group>
            <Group title={`Functions (${functions.length})`}>
              {functions.map((r) => (
                <button key={`${r.schema}.${r.name}`} className="tree-item" onClick={() => props.onOpenRoutine(r)}>
                  {r.schema}.{r.name} <span className="hint">{r.kind}</span>
                </button>
              ))}
            </Group>
          </>
        )}
      </section>
    </aside>
  );
}

function Group({ title, children }: { title: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(true);
  return (
    <div className="group">
      <button className="group-title" onClick={() => setOpen(!open)}>
        {open ? '▾' : '▸'} {title}
      </button>
      {open && <div className="group-body">{children}</div>}
    </div>
  );
}

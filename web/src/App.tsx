import { useCallback, useEffect, useState } from 'react';
import { api, getSession, setSession, type Session } from './api/client';
import type { ConnectionDto, DatabaseSchema, RoutineModel, TableModel } from './api/types';
import { LoginView } from './components/LoginView';
import { Sidebar } from './components/Sidebar';
import { SqlTab } from './components/SqlTab';
import { TableTab } from './components/TableTab';

type Tab =
  | { kind: 'table'; key: string; title: string; table: TableModel }
  | { kind: 'sql'; key: string; title: string; text: string; readOnly: boolean; canSave: boolean }
  | { kind: 'routine'; key: string; title: string; text: string };

let tabCounter = 0;

export function App() {
  const [session, setSessionState] = useState<Session | null>(getSession());
  const [connections, setConnections] = useState<ConnectionDto[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [schema, setSchema] = useState<DatabaseSchema | null>(null);
  const [schemaLoading, setSchemaLoading] = useState(false);
  const [schemaError, setSchemaError] = useState<string | null>(null);
  const [tabs, setTabs] = useState<Tab[]>([]);
  const [activeKey, setActiveKey] = useState<string | null>(null);

  // Clears the signed-in state when the refresh token is rejected anywhere in the app.
  useEffect(() => {
    const expire = () => setSessionState(null);
    window.addEventListener('dbexplorer:session-expired', expire);
    return () => window.removeEventListener('dbexplorer:session-expired', expire);
  }, []);

  const loadConnections = useCallback(async () => {
    setConnections(await api<ConnectionDto[]>('/api/connections'));
  }, []);

  useEffect(() => {
    if (session) loadConnections().catch(() => setConnections([]));
  }, [session, loadConnections]);

  const loadSchema = useCallback(async (id: string, refresh: boolean) => {
    setSchemaLoading(true);
    setSchemaError(null);
    try {
      setSchema(await api<DatabaseSchema>(`/api/connections/${id}/schema${refresh ? '?refresh=true' : ''}`));
    } catch (err) {
      setSchema(null);
      setSchemaError(err instanceof Error ? err.message : 'Could not load the schema.');
    } finally {
      setSchemaLoading(false);
    }
  }, []);

  function selectConnection(id: string) {
    setSelectedId(id);
    setTabs([]);
    setActiveKey(null);
    setSchema(null);
    void loadSchema(id, false);
  }

  function openTab(tab: Tab) {
    setTabs((prev) => (prev.some((t) => t.key === tab.key) ? prev : [...prev, tab]));
    setActiveKey(tab.key);
  }

  function openTable(table: TableModel) {
    openTab({ kind: 'table', key: `table:${table.schema}.${table.name}`, title: `${table.schema}.${table.name}`, table });
  }

  function openRoutine(routine: RoutineModel) {
    openTab({
      kind: 'routine',
      key: `routine:${routine.schema}.${routine.name}`,
      title: `${routine.schema}.${routine.name}`,
      text: routine.definition ?? '-- Definition is not available.',
    });
  }

  function openSql(title = 'SQL', text = '') {
    tabCounter += 1;
    openTab({ kind: 'sql', key: `sql:${tabCounter}`, title: `${title} ${tabCounter}`, text, readOnly: false, canSave: true });
  }

  function openScript(title: string, text: string) {
    tabCounter += 1;
    openTab({ kind: 'sql', key: `script:${tabCounter}`, title, text, readOnly: false, canSave: false });
  }

  function closeTab(key: string) {
    setTabs((prev) => {
      const next = prev.filter((t) => t.key !== key);
      if (activeKey === key) setActiveKey(next.length ? next[next.length - 1].key : null);
      return next;
    });
  }

  function signOut() {
    setSession(null);
    setSessionState(null);
    setConnections([]);
    setSelectedId(null);
    setSchema(null);
    setTabs([]);
    setActiveKey(null);
  }

  if (!session) {
    return <LoginView onSignedIn={(s) => setSessionState(s)} />;
  }

  const connectionId = selectedId;

  return (
    <div className="app">
      <header className="topbar">
        <strong className="brand">DbExplorer</strong>
        <select value={selectedId ?? ''} onChange={(e) => e.target.value && selectConnection(e.target.value)}>
          <option value="">Select a connection…</option>
          {connections.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name} ({c.host}{c.database ? ` / ${c.database}` : ''})
            </option>
          ))}
        </select>
        {selectedId && (
          <span className="hint">
            {schema ? `Schema loaded ${new Date(schema.loadedAt).toLocaleTimeString()}` : ''}
          </span>
        )}
        <span className="grow" />
        <button className="small" onClick={signOut}>
          Sign out
        </button>
      </header>

      <div className="body">
        <Sidebar
          connections={connections}
          selectedId={selectedId}
          schema={schema}
          schemaLoading={schemaLoading}
          onSelectConnection={selectConnection}
          onConnectionsChanged={loadConnections}
          onRefreshSchema={() => selectedId && loadSchema(selectedId, true)}
          onOpenTable={openTable}
          onOpenRoutine={openRoutine}
          onOpenSql={() => openSql()}
        />

        <main className="workspace">
          {schemaError && <div className="error">{schemaError}</div>}
          <div className="tab-strip">
            {tabs.map((t) => (
              <div key={t.key} className={`tab ${t.key === activeKey ? 'active' : ''}`}>
                <button className="tab-title" onClick={() => setActiveKey(t.key)}>
                  {t.title}
                </button>
                <button className="tab-close" onClick={() => closeTab(t.key)} aria-label="Close tab">
                  ×
                </button>
              </div>
            ))}
          </div>

          {!connectionId && <div className="empty">Choose a connection to start exploring.</div>}
          {connectionId &&
            tabs.map((t) => {
              if (t.kind === 'table') {
                return (
                  <TableTab
                    key={t.key}
                    connectionId={connectionId}
                    table={t.table}
                    active={t.key === activeKey}
                    onOpenScript={openScript}
                  />
                );
              }
              return (
                <SqlTab
                  key={t.key}
                  connectionId={connectionId}
                  title={t.title}
                  initialText={t.text}
                  readOnly={t.kind === 'routine' || (t.kind === 'sql' && t.readOnly)}
                  active={t.key === activeKey}
                  canSave={t.kind === 'sql' && t.canSave}
                />
              );
            })}
        </main>
      </div>
    </div>
  );
}

---
name: 'DbExplorer Web'
description: 'Owns the React + Vite + TypeScript SPA in web/: API client, login, connections, schema tree, AG Grid table tabs, Monaco SQL tabs, relation and lookup popups. Use for any UI change in web/.'
tools: ['codebase', 'editFiles', 'runCommands', 'search', 'problems']
---

# DbExplorer web agent

You own `web/`: `src/api/` (client and types), `src/components/`, `src/utils/`, `src/App.tsx`, `src/main.tsx`, `src/styles.css`.

## Layout and conventions

- `src/api/client.ts` is the only place that calls `fetch`. It adds the bearer token, refreshes once on 401, and dispatches `dbexplorer:session-expired` when the refresh fails. Use `api<T>` and `postJson<T>`.
- `src/api/types.ts` mirrors the server contracts (`src/DbExplorer.Application/Contracts/`). Keep field names camelCase and in step with the C# records. When a contract changes, change this file in the same commit.
- `App.tsx` owns session, connections, schema, and the tab list. A tab is `{ kind: 'table' | 'sql' | 'routine', key, title, ... }`. Tabs stay mounted and are hidden with the `hidden` attribute, so grid state survives tab switches.
- `TableTab` uses the AG Grid **infinite** row model. The datasource calls `/rows`. AG Grid's datasource callbacks are `successCallback` and `failCallback`. The React wrapper does not re-apply a changed datasource, so `TableTab` calls `setGridOption('datasource', ...)` in an effect. Keep that effect.
- `toApiFilterModel` (`src/utils/filters.ts`) maps AG Grid date filters (`dateFrom`/`dateTo`) to the API's `filter`/`filterTo`.
- Edits are batched in state (`pending`, `inserts`) and sent as one `/changes` request. Deletes are sent immediately, after a confirm.
- Ctrl+F and Ctrl+Enter are handled with a capture-phase `keydown` listener on the tab root. AG Grid and Monaco stop the event otherwise. Keep it in capture phase.
- Monaco is bundled through `loader.config({ monaco })` in `main.tsx`. Do not switch back to the CDN.
- AG Grid `headerCheckbox` is not supported by the infinite model. Do not enable it.
- Styling lives in `styles.css` with plain class names. There is no CSS framework.
- Only comment non-obvious code.

## Verify

1. `cd web` then `npm run build` (runs `tsc --noEmit` and `vite build`).
2. For UI behaviour, run the API (`dotnet run --project src\DbExplorer.Api --urls http://localhost:5105`) and `npm run dev`, then check the page in the browser tools. Use a test connection to the fixture database.
3. Note that the integrated browser captures Ctrl+F for its own find. Test the toolbar Find button instead.

## Boundaries

- Do not store the refresh token in `localStorage`. The current design uses `sessionStorage`.
- Do not add a new endpoint from the web side. Ask for it through the `api` agent.
- Keep `web/node_modules` and `web/dist` out of git (both are in `.gitignore`).

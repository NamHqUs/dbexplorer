export interface Session {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
}

const STORAGE_KEY = 'dbexplorer.session';

let current: Session | null = readStored();

function readStored(): Session | null {
  const raw = sessionStorage.getItem(STORAGE_KEY);
  return raw ? (JSON.parse(raw) as Session) : null;
}

export function getSession(): Session | null {
  return current;
}

export function setSession(session: Session | null): void {
  current = session;
  if (session) sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
  else sessionStorage.removeItem(STORAGE_KEY);
}

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly body: unknown,
  ) {
    super(message);
  }
}

interface ProblemBody {
  title?: string;
  detail?: string;
  errors?: unknown;
}

async function errorFrom(response: Response): Promise<ApiError> {
  let body: ProblemBody | undefined;
  try {
    body = (await response.json()) as ProblemBody;
  } catch {
    body = undefined;
  }
  const message = body?.detail ?? body?.title ?? `Request failed with status ${response.status}.`;
  return new ApiError(response.status, message, body);
}

// Refreshes the access token once. Concurrent 401s share the same refresh call.
let refreshing: Promise<boolean> | null = null;

async function refreshSession(): Promise<boolean> {
  if (!current) return false;
  refreshing ??= (async () => {
    const response = await fetch('/api/auth/refresh', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: current!.refreshToken }),
    });
  if (!response.ok) {
      setSession(null);
      window.dispatchEvent(new Event('dbexplorer:session-expired'));
      return false;
    }
    setSession((await response.json()) as Session);
    return true;
  })().finally(() => {
    refreshing = null;
  });
  return refreshing;
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const send = () => {
    const headers = new Headers(init.headers);
    if (init.body !== undefined) headers.set('Content-Type', 'application/json');
    if (current) headers.set('Authorization', `Bearer ${current.accessToken}`);
    return fetch(path, { ...init, headers });
  };

  let response = await send();
  if (response.status === 401 && (await refreshSession())) {
    response = await send();
  }
  if (!response.ok) throw await errorFrom(response);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export async function postJson<T>(path: string, body: unknown): Promise<T> {
  return api<T>(path, { method: 'POST', body: JSON.stringify(body) });
}

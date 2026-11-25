// Mirrors the API contracts in src/DbExplorer.Application/Contracts.

export type DbAuthMode = 'SqlAuth' | 'WindowsAuth';

export interface ConnectionDto {
  id: string;
  name: string;
  host: string;
  port: number | null;
  database: string | null;
  authMode: DbAuthMode;
  userName: string | null;
  hasPassword: boolean;
  readOnly: boolean;
  trustServerCertificate: boolean;
  timeoutSec: number;
  updatedAt: string;
}

export interface ConnectionInput {
  name: string;
  host: string;
  port: number | null;
  database: string | null;
  authMode: DbAuthMode;
  userName: string | null;
  password: string | null;
  readOnly: boolean;
  trustServerCertificate: boolean;
  timeoutSec: number;
}

export interface TestConnectionResult {
  success: boolean;
  serverVersion: string | null;
  error: string | null;
  elapsedMs: number;
}

export interface ColumnModel {
  name: string;
  ordinal: number;
  sqlType: string;
  isNullable: boolean;
  isIdentity: boolean;
  isComputed: boolean;
  isRowVersion: boolean;
}

export interface ForeignKeyModel {
  name: string;
  parentSchema: string;
  parentTable: string;
  parentColumns: string[];
  referencedSchema: string;
  referencedTable: string;
  referencedColumns: string[];
  onDelete: string;
}

export interface TableModel {
  schema: string;
  name: string;
  isView: boolean;
  definition: string | null;
  columns: ColumnModel[];
  primaryKey: string[];
  foreignKeys: ForeignKeyModel[];
  referencedBy: ForeignKeyModel[];
}

export interface ParameterModel {
  name: string;
  sqlType: string;
  isOutput: boolean;
}

export interface RoutineModel {
  schema: string;
  name: string;
  kind: string;
  definition: string | null;
  parameters: ParameterModel[];
}

export interface DatabaseSchema {
  loadedAt: string;
  tables: TableModel[];
  procedures: RoutineModel[];
  functions: RoutineModel[];
}

export type Row = Record<string, unknown>;

export interface TableRowsResponse {
  rows: Row[];
  lastRow: number | null;
  hasPrimaryKey: boolean;
}

export interface ChangeError {
  group: string;
  index: number;
  message: string;
}

export interface ChangeBatchResult {
  inserted: Row[];
  updated: Row[];
  deleted: number;
}

export interface LookupValue {
  value: unknown;
  count: number;
}

export interface LookupResponse {
  mode: 'foreignKey' | 'distinct';
  targetSchema: string | null;
  targetTable: string | null;
  targetColumn: string | null;
  rows: Row[] | null;
  values: LookupValue[] | null;
}

export interface RelationNode {
  id: string;
  schema: string;
  table: string;
  depth: number;
  row: Row;
}

export interface RelationEdge {
  childId: string;
  parentId: string;
  foreignKey: string;
}

export interface RelationGraph {
  nodes: RelationNode[];
  edges: RelationEdge[];
  warnings: string[];
}

export interface RelationRequest {
  schema: string;
  table: string;
  keys: Record<string, unknown>[];
  maxDepth: number;
  maxRowsPerEdge: number;
}

export interface CascadeScriptResponse {
  statements: string[];
  warnings: string[];
  truncated: boolean;
}

export interface ColumnInfo {
  name: string;
  sqlType: string;
}

export interface ResultSet {
  columns: ColumnInfo[];
  rows: unknown[][];
  truncated: boolean;
}

export interface QueryResponse {
  resultSets: ResultSet[];
  rowsAffected: number;
  durationMs: number;
}

export interface SavedQuery {
  id: string;
  name: string;
  folder: string | null;
  sqlText: string;
  connectionId: string | null;
  updatedAt: string;
}

export interface SavedQueryInput {
  name: string;
  folder: string | null;
  sqlText: string;
  connectionId: string | null;
}

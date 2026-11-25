import type { ColumnModel } from '../api/types';

const NUMERIC = new Set(['int', 'bigint', 'smallint', 'tinyint', 'decimal', 'numeric', 'money', 'smallmoney', 'float', 'real']);
const DATE = new Set(['date', 'datetime', 'datetime2', 'smalldatetime', 'datetimeoffset']);

export function baseType(sqlType: string): string {
  return sqlType.split('(')[0].toLowerCase();
}

export function agFilterFor(column: ColumnModel): 'agNumberColumnFilter' | 'agDateColumnFilter' | 'agTextColumnFilter' {
  const type = baseType(column.sqlType);
  if (NUMERIC.has(type)) return 'agNumberColumnFilter';
  if (DATE.has(type)) return 'agDateColumnFilter';
  return 'agTextColumnFilter';
}

// Maps AG Grid filter models to the API contract. Date filters use dateFrom/dateTo in AG Grid
// and filter/filterTo in the API. Combined (multi-condition) filters send their first condition only.
export function toApiFilterModel(model: Record<string, unknown> | null | undefined): Record<string, unknown> | undefined {
  if (!model) return undefined;
  const result: Record<string, unknown> = {};
  for (const [colId, raw] of Object.entries(model)) {
    const filter = raw as Record<string, unknown>;
    const condition = (Array.isArray(filter.conditions) ? filter.conditions[0] : filter) as Record<string, unknown>;
    if (condition.filterType === 'date') {
      result[colId] = { ...condition, filter: condition.dateFrom, filterTo: condition.dateTo };
    } else {
      result[colId] = condition;
    }
  }
  return Object.keys(result).length > 0 ? result : undefined;
}

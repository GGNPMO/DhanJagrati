/** Thin fetch wrapper for the same-origin /api. Errors are surfaced as ApiError (RFC 7807). */

export interface ProblemDetails {
  status?: number
  title?: string
  detail?: string
  code?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails

  constructor(status: number, problem: ProblemDetails) {
    super(problem.title ?? problem.detail ?? `Request failed (${status})`)
    this.status = status
    this.problem = problem
  }

  /** Field errors keyed by camelCase field name. */
  get fieldErrors(): Record<string, string> {
    const result: Record<string, string> = {}
    for (const [key, messages] of Object.entries(this.problem.errors ?? {})) {
      result[key] = messages.join(' ')
    }
    return result
  }
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE'

async function request<T>(method: Method, url: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (method !== 'GET') headers['X-DJ-Client'] = 'web'
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(url, {
    method,
    headers,
    credentials: 'same-origin',
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (response.status === 204) return undefined as T

  const isJson = response.headers.get('content-type')?.includes('json') ?? false
  const payload: unknown = isJson ? await response.json() : undefined

  if (!response.ok) {
    const problem = (payload as ProblemDetails | undefined) ?? { title: response.statusText }
    const error = new ApiError(response.status, problem)
    if (response.status === 401) window.dispatchEvent(new CustomEvent('dj:unauthorized'))
    if (response.status === 403 && problem.code === 'PASSWORD_CHANGE_REQUIRED')
      window.dispatchEvent(new CustomEvent('dj:password-change-required'))
    throw error
  }
  return payload as T
}

export const api = {
  get: <T>(url: string) => request<T>('GET', url),
  post: <T = void>(url: string, body?: unknown) => request<T>('POST', url, body ?? {}),
  put: <T = void>(url: string, body?: unknown) => request<T>('PUT', url, body ?? {}),
  delete: <T = void>(url: string) => request<T>('DELETE', url),
}

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message
  if (error instanceof Error) return error.message
  return 'Something went wrong.'
}

export function query(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== null && value !== undefined && value !== '') search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

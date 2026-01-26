import { ApiAuth } from './auth'
import type { ApiError } from './types'

// Error wrapper
export class ApiErrorResult<T = unknown> {
  constructor(public readonly error: T) {}
}

export type ApiResult<T> = T | ApiErrorResult<ApiError | Error>

// API client singleton
class ApiClient {
  auth: ApiAuth = ApiAuth.Anonymous
  endpoint: string = '/api'

  async request<T>(path: string): Promise<ApiResult<T>> {
    const headers: HeadersInit = {}
    
    if (!this.auth.isAnonymous) {
      headers['Authorization'] = this.auth.getBasicAuth()
    }

    try {
      const response = await fetch(this.endpoint + path, {
        method: 'GET',
        mode: 'cors',
        cache: 'no-cache',
        credentials: 'same-origin',
        headers,
      })

      if (response.status === 204) {
        return {} as T
      }

      const json = await response.json()

      if (!response.ok) {
        return new ApiErrorResult(json as ApiError)
      }

      return json as T
    } catch (err) {
      return new ApiErrorResult(err as Error)
    }
  }
}

export const api = new ApiClient()

// API path builder
export class ApiPath<T = unknown> {
  private path: string

  constructor(path: string = '') {
    this.path = path
  }

  static cmd<T>(...params: (string | number | boolean | ApiPath)[]): ApiPath<T> {
    let path = ''
    for (const param of params) {
      if (typeof param === 'string') {
        path += '/' + encodeURIComponent(param).replace(/\(/g, '%28').replace(/\)/g, '%29')
      } else if (typeof param === 'number' || typeof param === 'boolean') {
        path += '/' + param.toString()
      } else if (param instanceof ApiPath) {
        path += '/(' + param.toString() + ')'
      }
    }
    return new ApiPath<T>(path)
  }

  toString(): string {
    return this.path
  }

  async fetch(): Promise<ApiResult<T>> {
    return api.request<T>(this.path)
  }
}

// Utility functions
export function cmd<T>(...params: (string | number | ApiPath)[]): ApiPath<T> {
  return ApiPath.cmd<T>(...params)
}

export function bot<T>(command: ApiPath<T>, botId: number | string): ApiPath<T> {
  const id = typeof botId === 'number' ? botId.toString() : botId
  return ApiPath.cmd<T>('bot', 'use', id, command)
}

export function jmerge<T extends ApiPath[]>(...commands: T): ApiPath<UnwrapApiPath<T>> {
  return ApiPath.cmd<UnwrapApiPath<T>>('json', 'merge', ...commands)
}

// Type helper for unwrapping ApiPath array
type UnwrapApiPath<T extends ApiPath[]> = {
  [K in keyof T]: T[K] extends ApiPath<infer U> ? U : T[K]
}

// Type guard for error result
export function isError<T>(result: ApiResult<T>): result is ApiErrorResult<ApiError | Error> {
  return result instanceof ApiErrorResult
}

// Get error message from result
export function getErrorMessage(result: ApiErrorResult<ApiError | Error>): string {
  if (result.error instanceof Error) {
    return result.error.message
  }
  return result.error.ErrorMessage || 'Unknown error'
}

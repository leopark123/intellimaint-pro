export interface LoginRequest {
  username: string
  password: string
}

export interface LoginResponse {
  token: string
  refreshToken: string
  username: string
  role: string
  expiresAt: number
  refreshExpiresAt: number
}

export interface AuthState {
  token: string | null
  refreshToken: string | null
  username: string | null
  role: string | null
  expiresAt: number | null
  refreshExpiresAt: number | null
  isAuthenticated: boolean
}

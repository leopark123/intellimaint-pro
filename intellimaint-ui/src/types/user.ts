export interface User {
  userId: string
  username: string
  displayName: string | null
  role: string
  enabled: boolean
  createdUtc: number
  lastLoginUtc: number | null
}

export interface CreateUserRequest {
  username: string
  password: string
  role: string
  displayName?: string
}

export interface UpdateUserRequest {
  displayName?: string
  role?: string
  enabled?: boolean
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

export interface ResetPasswordRequest {
  newPassword: string
}

export const UserRoles = {
  Admin: 'Admin',
  Operator: 'Operator',
  Viewer: 'Viewer'
} as const

export const UserRoleOptions = [
  { value: 'Admin', label: '管理员' },
  { value: 'Operator', label: '操作员' },
  { value: 'Viewer', label: '查看者' }
]

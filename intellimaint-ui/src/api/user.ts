import apiClient from './client'
import type { ApiResponse } from '../types/telemetry'
import type { User, CreateUserRequest, UpdateUserRequest, ChangePasswordRequest, ResetPasswordRequest } from '../types/user'

export async function getUsers(): Promise<ApiResponse<User[]>> {
  return apiClient.get('/users')
}

export async function getUser(userId: string): Promise<ApiResponse<User>> {
  return apiClient.get(`/users/${encodeURIComponent(userId)}`)
}

export async function getCurrentUser(): Promise<ApiResponse<User>> {
  return apiClient.get('/users/me')
}

export async function createUser(request: CreateUserRequest): Promise<ApiResponse<User>> {
  return apiClient.post('/users', request)
}

export async function updateUser(userId: string, request: UpdateUserRequest): Promise<ApiResponse<User>> {
  return apiClient.put(`/users/${encodeURIComponent(userId)}`, request)
}

export async function disableUser(userId: string): Promise<ApiResponse<void>> {
  return apiClient.delete(`/users/${encodeURIComponent(userId)}`)
}

export async function enableUser(userId: string): Promise<ApiResponse<User>> {
  return apiClient.put(`/users/${encodeURIComponent(userId)}`, { enabled: true })
}

export async function changePassword(request: ChangePasswordRequest): Promise<ApiResponse<void>> {
  return apiClient.put('/users/password', request)
}

export async function resetPassword(userId: string, request: ResetPasswordRequest): Promise<ApiResponse<void>> {
  return apiClient.post(`/users/${encodeURIComponent(userId)}/reset-password`, request)
}

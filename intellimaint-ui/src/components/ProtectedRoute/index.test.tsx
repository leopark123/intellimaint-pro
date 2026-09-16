import { renderToString } from 'react-dom/server'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import ProtectedRoute from './index'

const state = vi.hoisted(() => ({ isAuthenticated: false }))
vi.mock('../../store/authStore', () => ({ useAuth: () => ({ auth: state }) }))
vi.mock('react-router-dom', async importOriginal => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  Navigate: ({ to, state: navState }: { to: string; state: { from: string } }) =>
    <span>{to}:{navState.from}</span>,
}))

describe('ProtectedRoute', () => {
  it('does not render protected content without authentication and preserves the requested path', () => {
    state.isAuthenticated = false
    const html = renderToString(<MemoryRouter initialEntries={['/alarms']}><ProtectedRoute>private content</ProtectedRoute></MemoryRouter>)
    expect(html).not.toContain('private content')
    expect(html).toContain('/login')
    expect(html).toContain('/alarms')
  })
  it('renders protected content after authentication', () => {
    state.isAuthenticated = true
    const html = renderToString(<MemoryRouter><ProtectedRoute>private content</ProtectedRoute></MemoryRouter>)
    expect(html).toContain('private content')
  })
})

import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { setTimeout as delay } from 'node:timers/promises'

const config = Object.fromEntries(readFileSync(new URL('../../.env', import.meta.url), 'utf8')
  .split(/\r?\n/).filter(s => s && !s.startsWith('#')).map(s => {
    const i = s.indexOf('='); return [s.slice(0, i), s.slice(i + 1)]
  }))
const base = process.env.DEMO_BASE_URL || 'http://localhost:5000'
let ready = false
for (let i = 0; i < 60; i++) {
  try { if ((await fetch(base + '/health/live', { signal: AbortSignal.timeout(2000) })).ok) { ready = true; break } } catch {}
  await delay(1000)
}
assert.ok(ready, 'API did not start within the deadline')
const request = async (path, options = {}) => fetch(base + path, { ...options, signal: AbortSignal.timeout(10000) })
assert.equal((await request('/api/telemetry/latest')).status, 401)
const login = await request('/api/auth/login', {
  method: 'POST', headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ username: config.ADMIN_USERNAME, password: config.ADMIN_PASSWORD }),
})
assert.equal(login.status, 200, 'Configured administrator could not sign in')
const token = (await login.json()).data.token
const headers = { Authorization: `Bearer ${token}` }
const device = 'synthetic-motor-001'
const paths = {
  telemetry: `/api/telemetry/latest?deviceId=${device}`,
  trend: `/api/telemetry/query?deviceId=${device}`,
  alarms: `/api/alarms?deviceId=${device}`,
  health: `/api/health-assessment/devices/${device}`,
}
const results = {}
// Host liveness can precede background demo seeding; wait for the required data, not a fixed sleep.
for (let attempt = 0; attempt < 60; attempt++) {
  for (const [name, path] of Object.entries(paths)) {
    const response = await request(path, { headers })
    assert.equal(response.status, 200, `${name} API must succeed`)
    results[name] = await response.json()
  }
  if (results.telemetry.data.length === 5 && results.trend.data.length >= 100 && results.alarms.data.items.length > 0) break
  await delay(500)
}
assert.equal(results.telemetry.data.length, 5)
assert.ok(results.trend.data.length >= 100)
assert.ok(results.alarms.data.items.length > 0)
assert.ok(Number.isFinite(results.health.data.index))
console.log('PASS: JWT login, five synthetic signals, trend history, generated alarm and health score. No PLC used.')

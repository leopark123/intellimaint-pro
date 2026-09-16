import { randomBytes } from 'node:crypto'
import { existsSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const root = new URL('../../', import.meta.url)
const target = new URL('.env', root)
if (existsSync(target)) {
  console.error('.env already exists; preserved. Review it or move it aside before initializing a fresh demo.')
  process.exitCode = 1
} else {
  const lines = [
    '# Local credentials. Never commit this file.',
    'ASPNETCORE_ENVIRONMENT=Development', 'DatabaseProvider=Sqlite',
    'Demo__Enabled=true', 'Edge__DatabasePath=data/demo.db',
    `JWT_SECRET_KEY=${randomBytes(48).toString('hex')}`,
    'ADMIN_USERNAME=demo_admin', `ADMIN_PASSWORD=${randomBytes(24).toString('hex')}`,
    'VITE_DEMO_MODE=true',
  ]
  writeFileSync(target, lines.join('\n') + '\n', { mode: 0o600, flag: 'wx' })
  console.log(`Created ${fileURLToPath(target)}. Read ADMIN_USERNAME/ADMIN_PASSWORD there to sign in. No password is printed to logs.`)
}

import { readFileSync } from 'node:fs'
import { spawn } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../../', import.meta.url))
const values = Object.fromEntries(readFileSync(new URL('../../.env', import.meta.url), 'utf8')
  .split(/\r?\n/).filter(line => line && !line.startsWith('#')).map(line => {
    const i = line.indexOf('='); return [line.slice(0, i), line.slice(i + 1)]
  }))
const env = { ...process.env, ...values }
const frontend = process.argv[2] === 'ui'
const executable = frontend ? process.execPath : (process.env.DOTNET_EXE || 'dotnet')
const args = frontend
  ? ['intellimaint-ui/node_modules/vite/bin/vite.js', 'intellimaint-ui', '--host', '127.0.0.1']
  : ['run', '--project', 'src/Host.Api', '--no-launch-profile', '--urls', 'http://localhost:5000']
const child = spawn(executable, args, { cwd: root, env, stdio: 'inherit' })
child.on('error', err => { console.error(err.message); process.exitCode = 1 })
child.on('exit', code => { process.exitCode = code ?? 1 })
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal))

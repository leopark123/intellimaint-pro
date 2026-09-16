// Dependency-free validation of local Markdown targets and heading fragments.
import { execFileSync } from 'node:child_process'
import { existsSync, readFileSync, statSync } from 'node:fs'
import { dirname, resolve, relative } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../', import.meta.url))
const files = [...new Set(execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], { cwd: root })
  .toString().split('\0').filter(p => p.endsWith('.md') && existsSync(resolve(root, p))))]
const errors = []
let links = 0
const withoutCode = text => text.replace(/```[\s\S]*?```/g, '').replace(/`[^`\n]+`/g, '')
function anchors(text) {
  const seen = new Map()
  return new Set([...text.matchAll(/^#{1,6}\s+(.+?)\s*#*$/gm)].map(([, title]) => {
    const slug = title.toLowerCase().replace(/<[^>]*>/g, '').replace(/[^\p{L}\p{N}\p{M}_\-\s]/gu, '').replace(/ /g, '-')
    const count = seen.get(slug) || 0; seen.set(slug, count + 1)
    return slug + (count ? `-${count}` : '')
  }))
}
for (const file of files) {
  const raw = readFileSync(resolve(root, file), 'utf8')
  const text = withoutCode(raw)
  // Inline/image and reference-definition links; external URLs are intentionally not fetched.
  const targets = [...text.matchAll(/\]\(\s*(<[^>]+>|[^\s)]+)(?:\s+["'][^\n]*?["'])?\s*\)/g)].map(m => m[1])
  targets.push(...[...text.matchAll(/^\s*\[[^\]]+\]:\s*(\S+)/gm)].map(m => m[1]))
  for (let target of targets) {
    target = target.replace(/^<|>$/g, '')
    if (/^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(target)) continue
    links++
    let decoded
    try { decoded = decodeURIComponent(target) } catch { errors.push(`${file}: invalid URL ${target}`); continue }
    const [pathAndQuery, fragment] = decoded.split('#')
    const path = pathAndQuery.split('?')[0]
    const absolute = path ? resolve(dirname(resolve(root, file)), path) : resolve(root, file)
    if (relative(root, absolute).startsWith('..')) { errors.push(`${file}: target outside repository ${target}`); continue }
    if (!existsSync(absolute)) { errors.push(`${file}: missing ${target}`); continue }
    if (fragment && absolute.endsWith('.md') && statSync(absolute).isFile()) {
      const content = readFileSync(absolute, 'utf8')
      if (!anchors(content).has(fragment) && !content.includes(`id="${fragment}"`) && !content.includes(`name="${fragment}"`))
        errors.push(`${file}: missing heading ${target}`)
    }
  }
}
if (errors.length) { console.error(errors.join('\n')); process.exitCode = 1 }
else console.log(`PASS: ${files.length} Markdown files, ${links} relative links and heading fragments. External URLs are not checked.`)

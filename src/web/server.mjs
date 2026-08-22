// 프로덕션용 정적 서빙 + 프록시 서버 (Azure Container Apps)
// Aspire 서비스 디스커버리 env(services__api__http__0 등)를 그대로 사용한다.
import http from 'node:http'
import { createReadStream, existsSync, statSync } from 'node:fs'
import { extname, join, normalize } from 'node:path'

const PORT = process.env.PORT ?? 80
const API = process.env.services__api__http__0 ?? 'http://api'
const AGENT = process.env.services__agent__http__0 ?? 'http://agent'
const DIST = join(import.meta.dirname, 'dist')

const MIME = {
  '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css',
  '.svg': 'image/svg+xml', '.png': 'image/png', '.ico': 'image/x-icon',
  '.woff2': 'font/woff2', '.json': 'application/json',
}

function proxy(req, res, target) {
  const url = new URL(req.url, target)
  const preq = http.request(url, { method: req.method, headers: { ...req.headers, host: url.host } }, (pres) => {
    res.writeHead(pres.statusCode, pres.headers)
    pres.pipe(res)
  })
  preq.on('error', () => { res.writeHead(502); res.end('upstream error') })
  req.pipe(preq)
}

http.createServer((req, res) => {
  const path = req.url.split('?')[0]
  if (path.startsWith('/api') || path.startsWith('/photos')) return proxy(req, res, API)
  if (path.startsWith('/agent')) return proxy(req, res, AGENT)

  let file = normalize(join(DIST, path === '/' ? 'index.html' : path))
  if (!file.startsWith(DIST) || !existsSync(file) || statSync(file).isDirectory()) {
    file = join(DIST, 'index.html') // SPA 폴백
  }
  res.writeHead(200, { 'Content-Type': MIME[extname(file)] ?? 'application/octet-stream' })
  createReadStream(file).pipe(res)
}).listen(PORT, () => console.log(`web serving on :${PORT} → api=${API} agent=${AGENT}`))

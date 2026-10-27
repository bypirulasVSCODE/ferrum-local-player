import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const HOST = '127.0.0.1';
const PORT = 43821;
const ROOT = dirname(fileURLToPath(import.meta.url));
const APP = resolve(ROOT, 'ferrum-player.html');

let page;
try {
  page = await readFile(APP);
} catch (error) {
  console.error('No se pudo leer ferrum-player.html:', error.message);
  process.exit(1);
}

const server = createServer((request, response) => {
  const pathname = new URL(request.url || '/', 'http://' + HOST + ':' + PORT).pathname;
  if (pathname === '/mini/state' && request.method === 'GET') { response.writeHead(204, { 'Cache-Control': 'no-store' }).end(); return; }
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    response.writeHead(405, { Allow: 'GET, HEAD' }).end();
    return;
  }
  if (pathname !== '/') {
    response.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' }).end('No encontrado');
    return;
  }
  response.writeHead(200, {
    'Content-Type': 'text/html; charset=utf-8',
    'Cache-Control': 'no-store',
    'Referrer-Policy': 'strict-origin-when-cross-origin',
    'X-Content-Type-Options': 'nosniff',
    'Content-Security-Policy': "default-src 'self'; script-src 'self' 'unsafe-inline'; worker-src 'self' blob:; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; media-src 'self' blob:; connect-src 'self'; form-action 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'"
  });
  if (request.method === 'HEAD') response.end();
  else response.end(page);
});

server.on('error', error => {
  if (error.code === 'EADDRINUSE') console.error('El puerto ' + PORT + ' ya está en uso. Cierra otra instancia de FERRUM.');
  else console.error('Error del servidor local:', error.message);
  process.exitCode = 1;
});

server.listen(PORT, HOST, () => {
  console.log('FERRUM disponible en http://' + HOST + ':' + PORT);
  console.log('Solo escucha en este ordenador. Cierra esta ventana para detenerlo.');
});



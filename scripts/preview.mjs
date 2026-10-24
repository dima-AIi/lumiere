import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { join, extname, resolve, sep } from 'node:path';
import { GET, POST, HEAD } from '../api/index.mjs';

const root=resolve('dist');
const types={'.html':'text/html; charset=utf-8','.css':'text/css','.js':'text/javascript','.json':'application/json','.svg':'image/svg+xml','.jpg':'image/jpeg','.jpeg':'image/jpeg','.png':'image/png','.mp4':'video/mp4'};
createServer(async (req,res) => {
  const url=new URL(req.url,'http://localhost');
  try {
    if (/^\/(backend\/|api\/|media\/)/.test(url.pathname)) {
      const chunks=[]; for await (const chunk of req) chunks.push(chunk);
      const request=new Request(url,{method:req.method,headers:req.headers,
        body:['GET','HEAD'].includes(req.method) ? undefined : Buffer.concat(chunks)});
      const handler={GET,POST,HEAD}[req.method];
      const response=handler ? await handler(request) : new Response('Method not allowed',{status:405});
      res.writeHead(response.status,Object.fromEntries(response.headers));
      res.end(Buffer.from(await response.arrayBuffer())); return;
    }
    const path=resolve(join(root,decodeURIComponent(url.pathname)));
    if (!path.startsWith(root+sep) && path!==root) {res.writeHead(403);res.end();return;}
    let file=path===root ? join(root,'index.html') : path;
    try { if ((await stat(file)).isDirectory()) file=join(file,'index.html'); }
    catch { if (!extname(file)) file+='.html'; }
    try { const body=await readFile(file); res.writeHead(200,{'Content-Type':types[extname(file)] || 'application/octet-stream'});res.end(body); }
    catch { res.writeHead(404,{'Content-Type':'text/html; charset=utf-8'});res.end(await readFile(join(root,'404.html'))); }
  } catch { res.writeHead(500);res.end('Preview failed'); }
}).listen(4173,'127.0.0.1',() => console.log('Lumiere public preview: http://127.0.0.1:4173'));

import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { join, extname, resolve, sep } from 'node:path';
import { demoResponse, securityHeaders } from '../demo-policy.mjs';
const root=resolve('dist');
const types={'.html':'text/html; charset=utf-8','.css':'text/css','.js':'text/javascript','.json':'application/json','.svg':'image/svg+xml','.jpg':'image/jpeg','.jpeg':'image/jpeg','.png':'image/png','.mp4':'video/mp4','.woff2':'font/woff2'};
createServer(async (req,res)=>{
  const url=new URL(req.url,'http://127.0.0.1');
  const blocked=demoResponse(new Request(url,{method:req.method}));
  if(blocked){res.writeHead(blocked.status,Object.fromEntries(blocked.headers));res.end();return;}
  try{
    let file=resolve(join(root,decodeURIComponent(url.pathname)));
    if(file!==root&&!file.startsWith(root+sep)){res.writeHead(403);res.end();return;}
    try{if((await stat(file)).isDirectory())file=join(file,'index.html');}
    catch{if(!extname(file))file+='.html';}
    const body=await readFile(file);
    res.writeHead(200,{...securityHeaders,'Content-Type':types[extname(file)]||'application/octet-stream'});
    res.end(req.method==='HEAD'?undefined:body);
  }catch{res.writeHead(404,{...securityHeaders,'Content-Type':'text/html; charset=utf-8'});res.end(await readFile(join(root,'404.html')));}
}).listen(4173,'127.0.0.1',()=>console.log('Lumiere demo: http://127.0.0.1:4173'));

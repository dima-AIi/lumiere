import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { readFile, readdir, stat } from 'node:fs/promises';
import { resolve } from 'node:path';
import { createHash } from 'node:crypto';
const files=['index.html','Home/Menu.html','Home/Contacts.html','Home/CartPage.html','Home/Checkout.html','Home/Privacy.html'];
for(const file of files){
  const html=await readFile('dist/'+file,'utf8');
  assert.doesNotMatch(html,/<form\b|name="(?:Name|Phone|Address|Comment|password|email|__RequestVerificationToken)"|href="\/Admin|server-gate|YooKassa|ЮKassa/i,file);
  assert.doesNotMatch(html,/<script(?![^>]*\bsrc=)[^>]*>/i,'No inline scripts; CSP remains strict');
}
const catalog=JSON.parse(await readFile('dist/data/menu.json','utf8'));
for(const item of catalog.categories.flatMap(c=>c.items))assert.ok((await stat('dist/img/'+item.img)).isFile(),item.img);
const client=await readFile('dist/js/public.js','utf8');
assert.equal((client.match(/\bfetch\(/g)||[]).length,1,'Only the static menu is fetched');
assert.match(client,/fetch\('\/data\/menu\.json'\)/);
assert.doesNotMatch(client,/\/backend|\/api|sendBeacon|XMLHttpRequest|history\.replaceState|FormData/);
const deps=await readFile('Lumiere/bin/Release/net10.0/Lumiere.deps.json','utf8');
assert.doesNotMatch(deps,/Npgsql|Postgres/);
const before=createHash('sha256').update(await readFile('Lumiere/wwwroot/data/menu.json')).digest('hex');
const server=spawn('dotnet',['bin/Release/net10.0/Lumiere.dll'],{cwd:resolve('Lumiere'),env:{...process.env,
  ASPNETCORE_ENVIRONMENT:'Production',DOTNET_ENVIRONMENT:'Production',PORT:'5186',
  DATABASE_URL:'postgresql://invalid:invalid@127.0.0.1:1/unreachable',Admin__Password:'unused-test-value',
  DOTNET_CLI_HOME:resolve('artifacts/dotnet-home'),DOTNET_CLI_TELEMETRY_OPTOUT:'1'},stdio:['ignore','pipe','pipe']});
let errors='';server.stderr.on('data',b=>errors+=b);server.stdout.on('data',()=>{});
const base='http://127.0.0.1:5186';
try{
  let ready=false;
  for(let n=0;n<80;n++){
    if(server.exitCode!==null)throw Error('Server failed: '+errors);
    try{const r=await fetch(base+'/healthz');if(r.ok){assert.equal((await r.json()).mode,'static-demo');ready=true;break;}}catch{}
    await new Promise(r=>setTimeout(r,250));
  }
  assert.ok(ready,'Server starts even with an unreachable DATABASE_URL');
  for(const path of ['/','/Home/Menu','/Home/CartPage','/Home/Checkout','/Home/Privacy']){
    const r=await fetch(base+path);assert.equal(r.status,200,path);assert.equal(r.headers.get('set-cookie'),null);
    assert.match(r.headers.get('content-security-policy'),/form-action 'none'/);
  }
  for(const path of ['/Admin','/Admin/Orders','/api/session','/api/cart/import','/backend/Home/Checkout','/register'])
    assert.equal((await fetch(base+path)).status,410,path);
  for(const method of ['POST','PUT','PATCH','DELETE'])for(const path of ['/','/Home/Checkout','/Home/AddToCart','/Admin/Login','/backend/api/cart/import']){
    const r=await fetch(base+path,{method,body:'synthetic-test-only'});assert.equal(r.status,405,method+' '+path);
    assert.equal(r.headers.get('set-cookie'),null);
  }
  const after=createHash('sha256').update(await readFile('Lumiere/wwwroot/data/menu.json')).digest('hex');
  assert.equal(after,before,'Attempts do not change the menu');
  assert.ok(!(await readdir('Lumiere')).includes('App_Data'),'No runtime order directory created');
  console.log('PASS: six form-free pages, all menu images, static-only fetch, no database dependency, no sessions, legacy routes gone, all write methods denied, no files changed.');
}finally{
  if(server.exitCode===null){const done=new Promise(r=>server.once('exit',r));server.kill();await done;}
}

// Integration verification in a newly created, isolated PostgreSQL schema.
// No production order, menu or session is modified.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import pg from 'pg';

const connectionString=process.env.DATABASE_URL_UNPOOLED || process.env.POSTGRES_URL_NON_POOLING || process.env.DATABASE_URL;
if (!connectionString) throw Error('Database environment is required');
const schema='lumiere_qa_'+randomUUID().replaceAll('-','');
assert.match(schema,/^lumiere_qa_[a-f0-9]{32}$/);
const client=new pg.Client({connectionString});
await client.connect();
await client.query(`CREATE SCHEMA "${schema}"`);
const adminPassword=randomUUID();
const folder=resolve('artifacts/qa-backend-v2');
const cookies=new Map();
let server;
const pause=ms=>new Promise(r=>setTimeout(r,ms));
async function start() {
  server=spawn('dotnet',['Lumiere.dll'],{cwd:folder,env:{...process.env,
    DATABASE_URL:connectionString,Postgres__Schema:schema,
    ASPNETCORE_ENVIRONMENT:'Development',DOTNET_ENVIRONMENT:'Development',PORT:'5184',
    Admin__Password:adminPassword,YooKassa__ShopId:'',YooKassa__SecretKey:''},stdio:['ignore','pipe','pipe']});
  let errors=''; server.stderr.on('data',chunk=>errors+=chunk); server.stdout.on('data',()=>{});
  for (let i=0;i<60;i++) {
    if (server.exitCode!==null) throw Error('Isolated backend failed to start: '+errors);
    try { const r=await fetch('http://127.0.0.1:5184/healthz'); if(r.ok)return; } catch {}
    await pause(500);
  }
  throw Error('Isolated backend readiness timed out');
}
async function stop() {
  if (!server || server.exitCode!==null)return;
  const done=new Promise(resolve=>server.once('exit',resolve));
  server.kill(); await done;
}
async function request(path,options={}) {
  const headers=new Headers(options.headers);
  headers.set('Cookie',[...cookies].map(([k,v])=>k+'='+v).join('; '));
  const response=await fetch('http://127.0.0.1:5184/backend'+path,{...options,headers,redirect:'manual'});
  for (const value of response.headers.getSetCookie()) { const pair=value.split(';')[0]; const split=pair.indexOf('='); cookies.set(pair.slice(0,split),pair.slice(split+1)); }
  assert.match(response.headers.get('cache-control') || '',/no-store/,'Private response must not be cached');
  return response;
}
const token=html=>html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
try {
  await start();
  const isolation=await client.query('SELECT count(*)::int AS count FROM information_schema.tables WHERE table_schema=$1',[schema]);
  assert.equal(isolation.rows[0].count,4,'Backend must create its tables only in the isolated QA schema');
  const unauthorized=await request('/Admin/Orders'); assert.equal(unauthorized.status,302);
  const missingCsrf=await request('/api/cart/import',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({lines:[],deliveryType:'pickup'})});
  assert.equal(missingCsrf.status,400);
  const session=await (await request('/api/session')).json();
  const menu=JSON.parse(await readFile('Lumiere/wwwroot/data/menu.json','utf8'));
  const item=menu.categories[0].items[0];
  const imported=await request('/api/cart/import',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':session.token},
    body:JSON.stringify({lines:[{id:item.id,qty:2}],deliveryType:'delivery'})});
  assert.equal(imported.status,200);
  assert.equal((await imported.json()).total,Math.round((item.price*2+menu.deliveryPrice)*100));
  let checkout=await (await request('/Home/Checkout')).text();
  assert.match(checkout,/SubmissionId/);
  await stop(); await start();
  checkout=await (await request('/Home/Checkout')).text();
  assert.match(checkout,/SubmissionId/,'Cart/session must survive restart');
  const submission=checkout.match(/name="SubmissionId"[^>]*value="([^"]+)"/)?.[1]; assert.ok(submission);
  const fields={Name:'Проверка публикации',Phone:'70000000000',DeliveryType:'pickup',SubmissionId:submission,__RequestVerificationToken:token(checkout)};
  const invalid=await request('/Home/Checkout',{method:'POST',body:new URLSearchParams({...fields,Name:'x'.repeat(100)})});
  assert.equal(invalid.status,200);
  const before=await client.query(`SELECT data FROM "${schema}".lumiere_documents WHERE key='orders'`);
  assert.equal(before.rows.length,0,'Invalid order must not be stored');
  const success=await request('/Home/Checkout',{method:'POST',body:new URLSearchParams(fields)});
  assert.equal(success.status,200); assert.match(await success.text(),/деньги не списаны/);
  const repeat=await request('/Home/Checkout',{method:'POST',body:new URLSearchParams(fields)}); assert.equal(repeat.status,400);
  const orders=await client.query(`SELECT data FROM "${schema}".lumiere_documents WHERE key='orders'`);
  assert.equal(orders.rows[0].data.length,1,'Only one order may be stored');
  let login=await (await request('/Admin/Login')).text();
  const signed=await request('/Admin/Login',{method:'POST',body:new URLSearchParams({password:adminPassword,__RequestVerificationToken:token(login)})});
  assert.equal(signed.status,302); assert.match(signed.headers.get('location'),/^\/backend\/Admin\//);
  assert.equal((await request('/Admin/Orders')).status,200);
  const create=await (await request('/Admin/MenuItemCreate')).text();
  const form=new FormData();
  for (const [k,v] of Object.entries({Name:'Проверочная позиция',PriceRubles:'123.45',Weight:'500',Category:menu.categories[0].title,__RequestVerificationToken:token(create)}))form.set(k,v);
  const image=await readFile('Lumiere/wwwroot/img/'+item.img);
  form.set('photo',new Blob([image],{type:'image/jpeg'}),'verification.jpg');
  const uploaded=await request('/Admin/MenuItemCreate',{method:'POST',body:form});
  if(uploaded.status!==302) {
    const html=await uploaded.text();
    const errors=[...html.matchAll(/<span[^>]*class="[^"]*field-error[^"]*"[^>]*>([\s\S]*?)<\/span>/g)].map(match=>match[1].replace(/<[^>]*>/g,'').replace(/&#x([a-f0-9]+);/gi,(_,n)=>String.fromCodePoint(parseInt(n,16))));
    throw Error('Upload validation failed: '+errors.join('; '));
  }
  const current=await client.query(`SELECT data FROM "${schema}".lumiere_documents WHERE key='menu'`);
  const newItem=current.rows[0].data.categories.flatMap(c=>c.items).find(i=>i.name==='Проверочная позиция');
  assert.ok(newItem); assert.equal(newItem.price,123.45); assert.match(newItem.img,/^upload-/);
  await stop(); await start();
  assert.equal((await request('/Admin/Orders')).status,200,'Admin session/key ring must survive restart');
  const photo=await request('/media/'+newItem.img); assert.equal(photo.status,200);
  assert.deepEqual(Buffer.from(await photo.arrayBuffer()),image,'Uploaded photo must survive restart');
  console.log('PASS: private cache headers, admin access, CSRF, server prices, durable cart, order validation, demo checkout, duplicate protection, durable admin session, menu decimals and durable JPEG upload.');
} finally {
  await stop();
  await client.query(`DROP SCHEMA "${schema}" CASCADE`);
  await client.end();
}

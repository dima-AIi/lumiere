import pg from 'pg';
import { attachDatabasePool } from '@vercel/functions';

let pool;
const privateHeaders = {
  'Cache-Control': 'private, no-store, max-age=0',
  'CDN-Cache-Control': 'no-store', 'Vercel-CDN-Cache-Control': 'no-store',
  'X-Content-Type-Options': 'nosniff'
};
const json = (body, status = 200) => Response.json(body, { status, headers: privateHeaders });
function database() {
  if (!process.env.DATABASE_URL) throw new Error('Database environment is not configured');
  if (!pool) {
    pool = new pg.Pool({ connectionString: process.env.DATABASE_URL, max: 3,
      connectionTimeoutMillis: 12000, idleTimeoutMillis: 10000 });
    attachDatabasePool(pool);
  }
  return pool;
}
const escape = value => value.replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function unavailable(request, configured) {
  if (request.method === 'GET' && request.headers.get('accept')?.includes('text/html')) {
    const retry = escape(new URL(request.url).pathname + new URL(request.url).search);
    const message = configured ? 'Сервер запускается после простоя. На бесплатном Render это может занять минуту или дольше.'
      : 'Серверная часть ещё не подключена. Меню и корзина доступны на сайте.';
    return new Response(`<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
      ${configured ? '<meta http-equiv="refresh" content="5">' : ''}<title>Lumière — подключение</title>
      <body style="margin:0;background:#f6f1e9;color:#34291f;font:18px/1.7 Georgia,serif"><main style="max-width:620px;margin:15vh auto;padding:24px">
      <h1>Lumière</h1><p>${message}</p><p>Эта страница обновится автоматически. Заказ ещё не отправлен.</p>
      <a href="${retry}">Проверить снова</a> · <a href="/">На главную</a></main></body></html>`,
      {status:503,headers:{...privateHeaders,'Content-Type':'text/html; charset=utf-8','Retry-After':'5'}});
  }
  return json({ ready:false, error: configured ? 'Сервер пока недоступен. Если заказ уже отправлялся, не повторяйте оплату: проверьте результат позже.' : 'Серверная часть ещё не подключена.' },503);
}

async function handle(request) {
  const url = new URL(request.url);
  const route = url.searchParams.get('__path') ?? url.pathname.replace(/^\//,'');
  if (route === 'api/catalog' && request.method === 'GET') {
    try {
      // Deliberately select only the public menu, never order/session documents.
      const result = await database().query("SELECT data FROM lumiere_documents WHERE key='menu'");
      return result.rows.length ? json(result.rows[0].data) : json({error:'Menu is not initialized'},503);
    } catch { console.error('Public catalog is unavailable'); return json({error:'Catalog is temporarily unavailable'},503); }
  }
  if (route.startsWith('media/') && request.method === 'GET') {
    const name = route.slice(6);
    if (!/^upload-[a-f0-9]{32}\.jpg$/.test(name)) return json({error:'Not found'},404);
    try {
      const result = await database().query('SELECT body FROM lumiere_media WHERE name=$1',[name]);
      if (!result.rows.length) return json({error:'Not found'},404);
      // UUID filenames never change; these images contain public menu photography only.
      return new Response(result.rows[0].body,{headers:{'Content-Type':'image/jpeg',
        'Cache-Control':'public, max-age=31536000, immutable','X-Content-Type-Options':'nosniff'}});
    } catch { console.error('Public image is unavailable'); return json({error:'Image is unavailable'},503); }
  }
  if (!route.startsWith('backend/')) return json({error:'Not found'},404);
  const path = route.slice(8);
  if (!/^(healthz|api\/(session|cart\/import)|Admin(?:\/[A-Za-z]+(?:\/[A-Za-z0-9_-]+)?)?|Home\/(Checkout|PaymentSuccess|PaymentPending|YooKassaWebhook|Error))\/?$/i.test(path))
    return json({error:'Not found'},404);
  let origin;
  try {
    origin = new URL(process.env.RENDER_API_ORIGIN);
    if (origin.protocol !== 'https:' || !origin.hostname.endsWith('.onrender.com') || origin.username || origin.password)
      return json({error:'Invalid backend configuration'},503);
  } catch { return unavailable(request,false); }
  const target = new URL('/backend/'+path,origin);
  for (const [key,value] of url.searchParams) if (key !== '__path') target.searchParams.append(key,value);
  const headers = new Headers();
  for (const name of ['accept','content-type','cookie','x-csrf-token','x-requested-with']) {
    const value = request.headers.get(name); if (value) headers.set(name,value);
  }
  try {
    const body = ['GET','HEAD'].includes(request.method) ? undefined : await request.arrayBuffer();
    const response = await fetch(target,{method:request.method,headers,body,redirect:'manual',
      signal:AbortSignal.timeout(path === 'healthz' ? 7000 : 65000)});
    // Render's own startup HTML must never be mistaken for our app or JSON API.
    if (response.headers.get('x-lumiere-backend') !== '1') return unavailable(request,true);
    const outgoing = new Headers(privateHeaders);
    for (const name of ['content-type','location','retry-after']) {
      let value = response.headers.get(name);
      if (name === 'location' && value?.startsWith(origin.origin)) value = value.slice(origin.origin.length);
      if (value) outgoing.set(name,value);
    }
    for (const cookie of response.headers.getSetCookie()) outgoing.append('set-cookie',cookie);
    return new Response(response.body,{status:response.status,headers:outgoing});
  } catch { console.error('Backend request did not complete',request.method,path); return unavailable(request,true); }
}
export const GET = handle;
export const POST = handle;
export const HEAD = handle;

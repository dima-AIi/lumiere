import pg from 'pg';
import { readFile } from 'node:fs/promises';

const connectionString=process.env.DATABASE_URL_UNPOOLED || process.env.POSTGRES_URL_NON_POOLING || process.env.DATABASE_URL;
if (!connectionString) throw new Error('Database environment is missing');
const client=new pg.Client({connectionString});
await client.connect();
try {
  await client.query('BEGIN');
  await client.query(await readFile('Lumiere/Migrations/001_durable_state.sql','utf8'));
  const menu=await readFile('Lumiere/wwwroot/data/menu.json','utf8');
  await client.query("INSERT INTO lumiere_documents(key,data) VALUES ('menu',$1::jsonb) ON CONFLICT DO NOTHING",[menu]);
  const file=process.argv[2];
  if (file) {
    const original=JSON.parse(await readFile(file,'utf8'));
    if (!Array.isArray(original) || original.some(o => typeof o.Id!=='string' || !o.Id)) throw new Error('Invalid original orders file');
    await client.query("INSERT INTO lumiere_documents(key,data) VALUES ('orders','[]') ON CONFLICT DO NOTHING");
    const {rows}=await client.query("SELECT data FROM lumiere_documents WHERE key='orders' FOR UPDATE");
    const orders=rows[0].data;
    const existing=new Set(orders.map(o=>o.Id));
    for (const order of original) if (!existing.has(order.Id)) { orders.push(order); existing.add(order.Id); }
    await client.query("UPDATE lumiere_documents SET data=$1::jsonb WHERE key='orders'",[JSON.stringify(orders)]);
    console.log('Original orders checked:',original.length,'; saved total:',orders.length);
  }
  await client.query('COMMIT');
  console.log('Additive migration complete. Existing menu and order IDs preserved.');
} catch(error) { await client.query('ROLLBACK'); throw new Error('Migration failed; changes rolled back',{cause:error}); }
finally { await client.end(); }

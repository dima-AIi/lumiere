// Run via `vercel env run -e production -- node scripts/provision-render.mjs <public-origin>`.
// Only the new Lumiere service is eligible; credentials are uploaded as a Render secret file.
import { randomBytes } from 'node:crypto';
import { writeFile, mkdir } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { resolve } from 'node:path';

const publicOrigin=new URL(process.argv[2]);
if (publicOrigin.protocol!=='https:' || !publicOrigin.hostname.endsWith('.vercel.app')) throw Error('Expected verified Lumiere Vercel origin');
if (!process.env.DATABASE_URL) throw Error('Lumiere database environment is missing');
const executable=resolve('artifacts/tools/render-cli/render.exe');
function cli(args) {
  const result=spawnSync(executable,args,{encoding:'utf8'});
  if(result.status!==0) throw Error('Render CLI operation failed: '+result.stderr.slice(0,1600));
  return JSON.parse(result.stdout);
}
const services=cli(['services','--output','json']);
const existing=services.find(entry=>entry.service?.name==='lumiere-api');
if(existing) { console.log(JSON.stringify({existing:true,id:existing.service.id,url:existing.service.serviceDetails.url})); process.exit(0); }
const secret={DATABASE_URL:process.env.DATABASE_URL,Admin:{Password:randomBytes(32).toString('base64url')},
  YooKassa:{ReturnUrl:publicOrigin.origin+'/Home/PaymentSuccess'}};
await mkdir('artifacts',{recursive:true});
await writeFile('artifacts/lumiere-runtime.json',JSON.stringify(secret),{mode:0o600});
const service=cli(['services','create','--name','lumiere-api','--type','web_service','--runtime','docker',
  '--repo','https://github.com/dima-AIi/lumiere','--branch','main','--region','frankfurt','--plan','free',
  '--num-instances','1','--health-check-path','/healthz','--auto-deploy=true',
  '--env-var','ASPNETCORE_ENVIRONMENT=Production','--secret-file','lumiere.json:artifacts/lumiere-runtime.json',
  '--output','json','--confirm']);
const value=service.service || service;
if(value.serviceDetails?.plan!=='free') throw Error('Unexpected Render plan; inspect new service before proceeding');
const result={id:value.id,name:value.name,url:value.serviceDetails.url,plan:value.serviceDetails.plan,dashboardUrl:value.dashboardUrl};
await writeFile('artifacts/render-resource.json',JSON.stringify(result,null,2));
console.log(JSON.stringify(result,null,2));

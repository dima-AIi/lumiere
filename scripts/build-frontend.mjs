import { cp, mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const output = join(root,'dist');
if (!output.startsWith(root) || output !== join(root,'dist')) throw new Error('Invalid build directory');
await rm(output,{recursive:true,force:true});
await mkdir(output,{recursive:true});
await cp(join(root,'Lumiere','wwwroot'),output,{recursive:true});
function run(args) {
  const result = spawnSync('dotnet',args,{cwd:join(root,'Lumiere'),stdio:'inherit',
    env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',DOTNET_ENVIRONMENT:'Development',
      DOTNET_CLI_HOME:join(root,'artifacts','dotnet-home')}});
  if (result.status !== 0) throw new Error('Public export failed');
}
run(['build','-c','Release','--nologo']);
run([join(root,'Lumiere','bin','Release','net10.0','Lumiere.dll'),
  '--StaticExport=true','--FrontendOutput='+output]);
const shell = await readFile(join(output,'index.html'),'utf8');
const page = (title,body) => shell.replace(/<title>.*?<\/title>/s,`<title>${title} — Lumière</title>`)
  .replace(/<main\b[^>]*>[\s\S]*?<\/main>/,`<main>${body}</main>`);
const wrap = (title,body) => `<section><div class="wrap"><div class="section-head"><h1 class="h2">${title}</h1><div class="rule"></div></div>${body}</div></section>`;
await writeFile(join(output,'404.html'),page('Страница не найдена',wrap('Страница не найдена','<p><a class="btn" href="/">На главную</a></p>')));
console.log('Static demo ready: public pages, browser cart and demo checkout. No admin, payments or database.');

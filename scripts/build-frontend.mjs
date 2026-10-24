import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const output = join(root,'dist');
await mkdir(output,{recursive:true});
await cp(join(root,'Lumiere','wwwroot'),output,{recursive:true});
function run(args) {
  const result = spawnSync('dotnet',args,{cwd:join(root,'Lumiere'),stdio:'inherit',
    env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',DOTNET_ENVIRONMENT:'Development'}});
  if (result.status !== 0) throw new Error('Public export failed');
}
run(['build','-c','Release','--nologo']);
run([join(root,'Lumiere','bin','Release','net10.0','Lumiere.dll'),
  '--StaticExport=true','--FrontendOutput='+output]);
const shell = await readFile(join(output,'index.html'),'utf8');
const page = (title,body) => shell.replace(/<title>.*?<\/title>/s,`<title>${title} — Lumière</title>`)
  .replace(/<main\b[^>]*>[\s\S]*?<\/main>/,`<main>${body}</main>`);
const wrap = (title,body) => `<section><div class="wrap"><div class="section-head"><h1 class="h2">${title}</h1><div class="rule"></div></div>${body}</div></section>`;
await writeFile(join(output,'Home','CartPage.html'),page('Корзина',wrap('Корзина','<div id="public-cart"></div>')));
const gate = (title,kind) => page(title,wrap(title,`<div id="server-gate" data-kind="${kind}" class="empty" aria-live="polite">
  <p id="server-status">Подключаем сервер… Меню и корзина уже доступны.</p>
  <p>На бесплатном Render запуск после простоя может занять минуту или дольше.</p>
  <button id="server-retry" class="btn btn-gold" type="button" hidden>Повторить подключение</button>
  <a class="btn" href="/Home/CartPage">Вернуться в корзину</a></div>`));
await writeFile(join(output,'Home','Checkout.html'),gate('Оформление заказа','checkout'));
await writeFile(join(output,'Home','PaymentSuccess.html'),gate('Проверяем оплату','payment'));
await writeFile(join(output,'Admin.html'),gate('Панель управления','admin'));
await writeFile(join(output,'404.html'),page('Страница не найдена',wrap('Страница не найдена','<p><a class="btn" href="/">На главную</a></p>')));
console.log('Public frontend ready: home, menu, contacts, privacy, cart, checkout and admin shell.');

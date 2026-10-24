/* The public storefront works without Render. Store only product IDs locally. */
(() => {
  'use strict';
  if (!window.LUMIERE_STATIC) return;
  const key = 'lumiere.cart.v1';
  const esc = text => String(text ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const rub = value => Number(value).toLocaleString('ru-RU',{maximumFractionDigits:2})+' ₽';
  const image = item => '/'+(item.img?.startsWith('upload-') ? 'media/' : 'img/')+encodeURIComponent(item.img || 'placeholder.svg');
  let catalog;
  let state = { lines:[], deliveryType:'pickup' };
  try {
    const saved = JSON.parse(localStorage.getItem(key));
    if (saved && Array.isArray(saved.lines)) state = {
      lines:saved.lines.filter(l => typeof l.id === 'string' && Number.isInteger(l.qty) && l.qty > 0 && l.qty <= 99).slice(0,100),
      deliveryType:saved.deliveryType === 'delivery' ? 'delivery' : 'pickup'
    };
  } catch { /* Empty cart when storage is unavailable or invalid. */ }
  function save() { try { localStorage.setItem(key,JSON.stringify(state)); } catch { /* Private browsing may disable persistence. */ } badge(); }
  function badge() { document.querySelectorAll('.cart-count').forEach(el => el.textContent = state.lines.reduce((sum,l) => sum+l.qty,0)); }
  function notice(text) {
    let el = document.getElementById('public-notice');
    if (!el) { el = document.createElement('p'); el.id='public-notice'; el.className='wrap'; el.setAttribute('role','status'); document.querySelector('main').prepend(el); }
    el.textContent=text;
  }
  function validCatalog(data) {
    return data && Array.isArray(data.categories) && data.categories.every(c => Array.isArray(c.items));
  }
  const initial = fetch('/data/menu.json').then(r => { if (!r.ok) throw Error(); return r.json(); }).then(data => {
    if (!validCatalog(data)) throw Error(); catalog=data; refresh(); return data;
  }).catch(() => { notice('Не удалось загрузить меню. Обновите страницу.'); return null; });
  // Refresh public content via Vercel/Neon; never wait for Render to show the site.
  initial.then(() => fetch('/api/catalog',{cache:'no-store',signal:AbortSignal.timeout(15000)}))
    .then(r => r.ok ? r.json() : null).then(data => { if (validCatalog(data)) { catalog=data; refresh(); } }).catch(() => {});
  const items = () => catalog.categories.flatMap(c => c.items);
  const find = id => items().find(i => i.id === id);
  function refresh() { badge(); renderMenu(); renderCart(); }
  function renderMenu() {
    const results = document.getElementById('menu-results');
    if (!results || !catalog) return;
    const form = document.getElementById('menu-search');
    const field = form.querySelector('[name=q]');
    if (!field.dataset.initialized) { field.value=new URLSearchParams(location.search).get('q') || ''; field.dataset.initialized='1'; }
    const query = field.value.trim().toLocaleLowerCase('ru-RU');
    const categories = catalog.categories.map(category => ({...category,items:category.items.filter(item =>
      !query || category.title.toLocaleLowerCase('ru-RU').includes(query) ||
      (item.name+' '+(item.desc || '')).toLocaleLowerCase('ru-RU').includes(query))})).filter(c => c.items.length);
    if (!categories.length) { results.innerHTML='<div class="empty"><h3>Ничего не нашлось</h3><p>Попробуйте другое слово.</p></div>'; return; }
    results.innerHTML=`<div class="menu-layout"><nav class="cat-nav" aria-label="Категории меню">${categories.map((c,i) =>
      `<a href="#category-${i}"><span>${esc(c.title)}</span><span>${c.items.length}</span></a>`).join('')}</nav><div>${categories.map((c,i) =>
      `<div class="cat-block" id="category-${i}"><div class="cat-title"><h2>${esc(c.title)}</h2><span>${c.items.length} позиций</span></div><div class="grid">${c.items.map(item =>
        `<article class="card"><div class="card-photo"><img src="${image(item)}" alt="${esc(item.name)}" loading="lazy">${item.weight ? `<span class="card-weight">${esc(item.weight)} г</span>` : ''}</div>
        <div class="card-body"><h3 class="card-name">${esc(item.name)}</h3>${item.desc ? `<p class="card-desc">${esc(item.desc)}</p>` : ''}<div class="card-foot"><span class="price">${rub(item.price)}</span>
        <form class="add-to-cart"><input type="hidden" name="id" value="${esc(item.id)}"><button type="submit" class="btn btn-gold btn-sm">В корзину</button></form></div></div></article>`).join('')}</div></div>`).join('')}</div></div>`;
  }
  function renderCart() {
    const cart = document.getElementById('public-cart');
    if (!cart || !catalog) return;
    if (!state.lines.length) { cart.innerHTML='<div class="empty"><h3>В корзине пока пусто</h3><p>Загляните в меню — там есть что попробовать.</p><a class="btn btn-gold" href="/Home/Menu">Открыть меню</a></div>'; return; }
    const missing = state.lines.filter(line => !find(line.id));
    const subtotal = state.lines.reduce((sum,line) => sum+(find(line.id)?.price || 0)*line.qty,0);
    const delivery = state.deliveryType === 'delivery' ? catalog.deliveryPrice : 0;
    cart.innerHTML=`${missing.length ? '<p role="alert">Некоторые позиции больше недоступны. Удалите их перед оформлением.</p>' : ''}<div class="cart-layout"><div>${state.lines.map(line => {
      const item = find(line.id);
      return `<div class="cart-line">${item ? `<img src="${image(item)}" alt="${esc(item.name)}">` : ''}<div><h4>${esc(item?.name || 'Позиция недоступна')}</h4><p>${item ? rub(item.price)+' за штуку' : ''}</p><div class="qty">
      <button data-qty="${line.qty-1}" data-id="${esc(line.id)}" aria-label="Меньше">−</button><span>${line.qty}</span><button data-qty="${line.qty+1}" data-id="${esc(line.id)}" aria-label="Больше">+</button></div></div>
      <div class="right">${item ? rub(item.price*line.qty) : ''}<button class="link-remove" data-qty="0" data-id="${esc(line.id)}">Убрать</button></div></div>`;
    }).join('')}<button class="btn" data-clear-cart>Очистить корзину</button> <a class="btn" href="/Home/Menu">Продолжить выбирать</a></div>
    <aside class="summary"><h3>Итого</h3><div class="delivery-tabs"><button data-delivery="pickup" class="${delivery ? '' : 'active'}">Самовывоз · 0 ₽</button>
    <button data-delivery="delivery" class="${delivery ? 'active' : ''}">Доставка · ${rub(catalog.deliveryPrice)}</button></div>
    <div class="sum-row"><span>Позиции</span><span>${rub(subtotal)}</span></div><div class="sum-row"><span>Доставка</span><span>${rub(delivery)}</span></div><div class="sum-row total"><span>К оплате</span><span>${rub(subtotal+delivery)}</span></div>
    ${missing.length ? '' : '<a class="btn btn-solid btn-block" href="/Home/Checkout">Оформить заказ</a>'}
    <p class="muted" style="margin-top:1rem">Корзина работает без сервера. При оформлении сервер проверит актуальные цены и наличие.</p></aside></div>`;
  }
  document.addEventListener('submit',async event => {
    const form=event.target;
    if (form.matches('.add-to-cart')) {
      event.preventDefault(); await initial; if (!catalog) return;
      const id=new FormData(form).get('id');
      if (!find(id)) { notice('Эта позиция больше недоступна. Проверьте меню.'); return; }
      const line=state.lines.find(l => l.id===id);
      if (line) line.qty=Math.min(99,line.qty+1); else if (state.lines.length<100) state.lines.push({id,qty:1});
      save(); const button=form.querySelector('button'); const label=button.textContent;
      button.textContent='В корзине ✓'; setTimeout(() => button.textContent=label,1200);
    } else if (form.id==='menu-search') { event.preventDefault(); search(); }
  });
  function search() {
    const q=document.querySelector('#menu-search [name=q]').value;
    history.replaceState(null,'',q ? '/Home/Menu?q='+encodeURIComponent(q) : '/Home/Menu'); renderMenu();
  }
  document.querySelector('#menu-search [name=q]')?.addEventListener('input',search);
  document.addEventListener('click',event => {
    const button=event.target.closest('button'); if (!button) return;
    if (button.hasAttribute('data-clear-cart')) state.lines=[];
    else if (button.hasAttribute('data-delivery')) state.deliveryType=button.dataset.delivery;
    else if (button.hasAttribute('data-qty')) {
      const line=state.lines.find(l => l.id===button.dataset.id); if (!line) return;
      line.qty=Math.max(0,Math.min(99,Number(button.dataset.qty))); state.lines=state.lines.filter(l => l.qty);
    } else return;
    save(); renderCart();
  });
  window.addEventListener('storage',event => { if (event.key===key) location.reload(); });
  badge();

  const gate=document.getElementById('server-gate');
  if (!gate) return;
  const status=document.getElementById('server-status');
  const retry=document.getElementById('server-retry');
  let connecting=false;
  const pause=ms => new Promise(resolve => setTimeout(resolve,ms));
  async function connect() {
    if (connecting) return; connecting=true; retry.hidden=true;
    await initial;
    if (gate.dataset.kind==='checkout' && !state.lines.length) { location.replace('/Home/CartPage'); return; }
    status.textContent='Подключаем сервер… Корзина сохранена в этом браузере.';
    const deadline=Date.now()+180000;
    let ready=false;
    while (Date.now()<deadline) {
      try {
        const response=await fetch('/backend/healthz',{cache:'no-store',signal:AbortSignal.timeout(9000)});
        if (response.ok && response.headers.get('content-type')?.includes('application/json') && (await response.json()).ready) { ready=true; break; }
      } catch { /* Render may be waking up. */ }
      status.textContent='Сервер ещё запускается. На бесплатном тарифе это может занять минуту или дольше. Вы можете вернуться в меню.';
      await pause(4000);
    }
    if (!ready) { status.textContent='Сервер пока недоступен. Заказ не отправлен; корзина сохранена. Попробуйте позже.'; retry.hidden=false; connecting=false; return; }
    try {
      if (gate.dataset.kind==='checkout') {
        const session=await fetch('/backend/api/session',{cache:'no-store'});
        if (!session.ok) throw Error('Не удалось начать защищённую сессию.');
        const {token}=await session.json();
        const response=await fetch('/backend/api/cart/import',{method:'POST',cache:'no-store',
          headers:{'Content-Type':'application/json','X-CSRF-TOKEN':token},body:JSON.stringify(state)});
        if (!response.ok) { const body=await response.json(); throw Error(body.error || 'Не удалось передать корзину.'); }
        location.replace('/backend/Home/Checkout');
      } else if (gate.dataset.kind==='payment') location.replace('/backend/Home/PaymentSuccess'+location.search);
      else location.replace('/backend/Admin');
    } catch(error) { status.textContent=error.message; retry.hidden=false; connecting=false; }
  }
  retry.addEventListener('click',connect); connect();
})();

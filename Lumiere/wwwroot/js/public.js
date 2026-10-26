/* The public storefront works without Render. Store only product IDs locally. */
(() => {
  'use strict';
  if (!window.LUMIERE_STATIC) return;
  const key = 'lumiere.cart.demo.v2';
  const esc = text => String(text ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const rub = value => Number(value).toLocaleString('ru-RU',{maximumFractionDigits:2})+' ₽';
  const image = item => '/img/'+encodeURIComponent(item.img || 'placeholder.svg');
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
  const items = () => catalog.categories.flatMap(c => c.items);
  const find = id => items().find(i => i.id === id);
  function refresh() { badge(); renderMenu(); renderCart(); renderDemo(); }
  function renderMenu() {
    const results = document.getElementById('menu-results');
    if (!results || !catalog) return;
    const form = document.getElementById('menu-search');
    const field = form.querySelector('[data-query]');
    if (!field.dataset.initialized) { field.value=new URLSearchParams(location.search).get('q') || ''; field.dataset.initialized='1'; }
    const query = field.value.trim().toLocaleLowerCase('ru-RU');
    const categories = catalog.categories.map(category => ({...category,items:category.items.filter(item =>
      !query || category.title.toLocaleLowerCase('ru-RU').includes(query) ||
      (item.name+' '+(item.desc || '')).toLocaleLowerCase('ru-RU').includes(query))})).filter(c => c.items.length);
    if (!categories.length) { results.innerHTML='<div class="empty"><h3>Ничего не нашлось</h3><p>Попробуйте другое слово.</p></div>'; return; }
    results.innerHTML=`<div class="menu-layout"><nav class="cat-nav" aria-label="Категории меню">${categories.map((c,i) =>
      `<a href="#category-${i}"><span>${esc(c.title)}</span><span>${c.items.length}</span></a>`).join('')}</nav><div>${categories.map((c,i) =>
      `<div class="cat-block" id="category-${i}"><div class="cat-title"><h2>${esc(c.title)}</h2><span>${c.items.length} позиций</span></div><div class="grid">${c.items.map(item =>
        `<article class="card"><div class="card-photo"><img src="${image(item)}" alt="${esc(item.name)}" loading="eager">${item.weight ? `<span class="card-weight">${esc(item.weight)} г</span>` : ''}</div>
        <div class="card-body"><h3 class="card-name">${esc(item.name)}</h3>${item.desc ? `<p class="card-desc">${esc(item.desc)}</p>` : ''}<div class="card-foot"><span class="price">${rub(item.price)}</span>
        <button type="button" data-add="${esc(item.id)}" class="btn btn-gold btn-sm">В корзину</button></div></div></article>`).join('')}</div></div>`).join('')}</div></div>`;
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
    <p class="muted" style="margin-top:1rem">Демонстрация оформления без списания денег.</p></aside></div>`;
  }
  function search() {
    renderMenu();
  }
  document.querySelector('#menu-search [data-query]')?.addEventListener('input',search);
  document.querySelector('#menu-search-button')?.addEventListener('click',search);
  document.querySelector('#menu-query')?.addEventListener('keydown',event => { if (event.key==='Enter') {event.preventDefault();search();} });
  document.addEventListener('click',async event => {
    const button=event.target.closest('button'); if (!button) return;
    if (button.hasAttribute('data-add')) {
      await initial; if (!catalog) return;
      const id=button.dataset.add; if (!find(id)) return;
      const line=state.lines.find(l => l.id===id);
      if (line) line.qty=Math.min(99,line.qty+1); else if (state.lines.length<100) state.lines.push({id,qty:1});
      save(); const label=button.textContent; button.textContent='В корзине ✓';
      setTimeout(() => button.textContent=label,1200); return;
    }
    if (button.hasAttribute('data-demo-complete')) {
      completePayment();
      return;
    }
    if (button.hasAttribute('data-clear-cart')) state.lines=[];
    else if (button.hasAttribute('data-delivery')) state.deliveryType=button.dataset.delivery;
    else if (button.hasAttribute('data-qty')) {
      const line=state.lines.find(l => l.id===button.dataset.id); if (!line) return;
      line.qty=Math.max(0,Math.min(99,Number(button.dataset.qty))); state.lines=state.lines.filter(l => l.qty);
    } else return;
    save(); renderCart();
  });
  window.addEventListener('storage',event => { if (event.key===key) location.reload(); });
  function currentOrder() {
    const lines=state.lines.filter(line=>find(line.id));
    const subtotal=lines.reduce((sum,line)=>sum+find(line.id).price*line.qty,0);
    const delivery=state.deliveryType==='delivery' ? catalog.deliveryPrice : 0;
    return {lines,subtotal,delivery,total:subtotal+delivery};
  }
  function renderDemo() {
    const target=document.getElementById('demo-checkout'); if (!target || !catalog) return;
    const order=currentOrder();
    if (!order.lines.length) {target.innerHTML='<div class="empty"><p>Сначала выберите блюда в меню.</p><a class="btn btn-gold" href="/Home/Menu">Открыть меню</a></div>';return;}
    const address=[catalog.address,catalog.city].filter(Boolean).join(', ');
    target.innerHTML=`<div class="checkout-layout"><div class="checkout-details"><h2 class="h3">Детали заказа</h2>
      <label for="checkout-name">Имя</label><input id="checkout-name" type="text" maxlength="60" autocomplete="off" spellcheck="false" placeholder="Например, Гость" aria-required="true" aria-describedby="checkout-name-error checkout-local-note">
      <p id="checkout-name-error" class="checkout-error" role="alert" hidden>Укажите имя для оформления.</p>
      <p class="checkout-hint" id="checkout-local-note">Имя используется только на этой странице и никуда не отправляется. Можно указать вымышленное.</p>
      <label for="checkout-address">${state.deliveryType==='delivery' ? 'Адрес доставки' : 'Адрес самовывоза'}</label>
      <input id="checkout-address" type="text" value="${esc(address)}" readonly aria-readonly="true">
      <p class="checkout-hint">В демонстрации используется фиксированный адрес кафе.</p>
      <a class="demo-back" href="/Home/CartPage">Изменить состав заказа</a></div>
      <aside class="summary"><h2 class="h3">Ваш заказ</h2>${order.lines.map(line=>`<div class="sum-row"><span>${esc(find(line.id).name)} × ${line.qty}</span><span>${rub(find(line.id).price*line.qty)}</span></div>`).join('')}
      <div class="sum-row"><span>Доставка</span><span>${rub(order.delivery)}</span></div><div class="sum-row total"><span>К оплате</span><span>${rub(order.total)}</span></div>
      <button type="button" class="btn btn-solid btn-block" data-demo-complete>Оплатить ${rub(order.total)}</button>
      <p class="payment-demo-note">Демонстрация оплаты — деньги не списываются.</p></aside></div>`;
    const nameField=document.getElementById('checkout-name');
    nameField.addEventListener('input',()=>{document.getElementById('checkout-name-error').hidden=true;nameField.removeAttribute('aria-invalid');});
    nameField.addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();completePayment();}});
  }
  function completePayment() {
    const input=document.getElementById('checkout-name');
    const name=input.value.trim().replace(/\s+/g,' ').slice(0,60);
    if (!name) {document.getElementById('checkout-name-error').hidden=false;input.setAttribute('aria-invalid','true');input.focus();return;}
    const order=currentOrder(); if (!order.lines.length) return;
    // The name is written only into the current DOM. Never add it to cart state,
    // storage, query parameters, logs or requests.
    document.getElementById('receipt-name').textContent=name;
    document.getElementById('receipt-number').textContent='Заказ № LUM-'+String(Date.now()).slice(-6);
    document.getElementById('receipt-delivery').textContent=state.deliveryType==='delivery'?'Доставка':'Самовывоз';
    document.getElementById('receipt-address').textContent=[catalog.address,catalog.city].filter(Boolean).join(', ');
    document.getElementById('receipt-items').innerHTML=order.lines.map(line=>`<div class="sum-row"><span>${esc(find(line.id).name)} × ${line.qty}</span><span>${rub(find(line.id).price*line.qty)}</span></div>`).join('');
    document.getElementById('receipt-total').textContent=rub(order.total);
    input.value='';
    document.getElementById('demo-checkout').hidden=true;
    document.getElementById('demo-complete').hidden=false;
    document.getElementById('demo-complete').scrollIntoView({block:'start',behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'auto':'smooth'});
  }
  badge();
})();

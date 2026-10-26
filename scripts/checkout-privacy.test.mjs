import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile } from 'node:fs/promises';
const source=await readFile(new URL('../Lumiere/wwwroot/js/public.js',import.meta.url),'utf8');

async function browserPage(storage=new Map([['lumiere.cart.demo.v2',JSON.stringify({lines:[{id:'coffee',qty:2}],deliveryType:'delivery'})]])){
  const calls=[],writes=[],events=new Map(),elements=new Map();
  const element=(id)=>({id,hidden:false,value:'',textContent:'',innerHTML:'',dataset:{},
    addEventListener(){},setAttribute(){},removeAttribute(){},focus(){this.focused=true;},scrollIntoView(){}});
  for(const id of ['public-cart','cart-badge','demo-checkout','demo-complete','checkout-name','checkout-name-error','checkout-address','receipt-name','receipt-number','receipt-delivery','receipt-address','receipt-items','receipt-total'])elements.set(id,element(id));
  const menu={address:'ул. Портовая, 15',city:'Армавир',deliveryPrice:199,categories:[{title:'Кофе',items:[{id:'coffee',name:'Капучино',price:190}]}]};
  const context=vm.createContext({window:{LUMIERE_STATIC:true,addEventListener(){}},
    document:{getElementById:id=>elements.get(id)||null,querySelectorAll:selector=>selector==='.cart-count'?[elements.get('cart-badge')]:[],querySelector:()=>null,addEventListener:(name,fn)=>events.set(name,fn)},
    localStorage:{getItem:key=>storage.get(key)||null,setItem:(key,value)=>{writes.push([key,value]);storage.set(key,value);}},
    fetch:async url=>{calls.push(url);return{ok:true,json:async()=>menu};},
    location:{search:''},URLSearchParams,setTimeout(){},matchMedia:()=>({matches:true})});
  vm.runInContext(source,context);
  await new Promise(resolve=>setImmediate(resolve));
  const pay=async()=>events.get('click')({target:{closest:()=>({hasAttribute:name=>name==='data-demo-complete'})}});
  return{calls,writes,elements,pay,storage};
}
test('name stays on the current page and is never sent or persisted',async()=>{
  const page=await browserPage();
  page.elements.get('checkout-name').value='Тестовый Гость';
  page.elements.get('checkout-address').value='changed by browser developer tools';
  await page.pay();
  assert.equal(page.elements.get('receipt-name').textContent,'Тестовый Гость');
  assert.equal(page.elements.get('receipt-address').textContent,'ул. Портовая, 15, Армавир');
  assert.equal(page.elements.get('receipt-total').textContent,'579 ₽');
  assert.equal(page.elements.get('demo-complete').hidden,false);
  assert.equal(page.elements.get('checkout-name').value,'');
  assert.deepEqual(page.calls,['/data/menu.json']);
  assert.deepEqual(page.writes,[['lumiere.cart.demo.v2',JSON.stringify({lines:[],deliveryType:'pickup'})]]);
  const reloaded=await browserPage(page.storage);
  assert.equal(reloaded.elements.get('receipt-name').textContent,'');
});
test('empty name does not complete payment; HTML-like names remain plain text',async()=>{
  const page=await browserPage();
  page.elements.get('demo-complete').hidden=true;
  await page.pay();
  assert.equal(page.elements.get('demo-complete').hidden,true);
  assert.equal(page.elements.get('checkout-name-error').hidden,false);
  assert.deepEqual(page.writes,[]);
  const name='<img src=x onerror=alert(1)>';
  page.elements.get('checkout-name').value=name;
  await page.pay();
  assert.equal(page.elements.get('receipt-name').textContent,name);
  assert.equal(page.elements.get('receipt-name').innerHTML,'');
  assert.deepEqual(page.calls,['/data/menu.json']);
});

test('successful payment clears the cart immediately and after navigation while keeping the receipt',async()=>{
  const page=await browserPage();
  assert.equal(page.elements.get('cart-badge').textContent,2);
  page.elements.get('checkout-name').value='Гость';
  await page.pay();
  assert.equal(page.elements.get('cart-badge').textContent,0);
  assert.equal(page.elements.get('receipt-total').textContent,'579 ₽');
  assert.match(page.elements.get('receipt-items').innerHTML,/Капучино × 2/);
  const cartPage=await browserPage(page.storage);
  assert.equal(cartPage.elements.get('cart-badge').textContent,0);
  assert.match(cartPage.elements.get('public-cart').innerHTML,/В корзине пока пусто/);
});

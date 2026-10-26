import test from 'node:test';
import assert from 'node:assert/strict';
import middleware from '../middleware.js';
import { demoResponse } from '../demo-policy.mjs';

test('all write methods are rejected without consuming the body, including method overrides',()=>{
  for(const method of ['POST','PUT','PATCH','DELETE','OPTIONS'])
    for(const path of ['/','/Home/Checkout','/api/cart/import','/backend/Admin/MenuItemCreate']){
      const request=new Request('https://demo.example'+path,{method,body:'synthetic-test-only',headers:{'X-HTTP-Method-Override':'GET'}});
      const response=middleware(request);
      assert.equal(response.status,405,method+' '+path);
      assert.equal(response.headers.get('allow'),'GET, HEAD');
      assert.equal(request.bodyUsed,false);
    }
});
test('legacy auth, database, upload and backend URLs are gone, including encoded/case variants',()=>{
  for(const path of ['/Admin','/Admin/Login','/ADMIN.html','/%41dmin/Orders','/register','/login','/Account/Register','/api/catalog','/api/index','/api/disabled','/media/upload-test.jpg','/backend/healthz','/backend/Home/Checkout'])
    for(const method of ['GET','HEAD'])assert.equal(demoResponse(new Request('https://demo.example'+path,{method})).status,410,path);
});
test('only read requests continue; no identity cookies are issued',()=>{
  for(const method of ['GET','HEAD'])for(const path of ['/','/Home/Menu','/Home/CartPage','/Home/Checkout','/data/menu.json','/img/logo.svg']){
    const response=middleware(new Request('https://demo.example'+path,{method}));
    assert.equal(response.headers.get('x-middleware-next'),'1');
    assert.equal(response.headers.get('set-cookie'),null);
    assert.match(response.headers.get('content-security-policy'),/form-action 'none'/);
  }
});

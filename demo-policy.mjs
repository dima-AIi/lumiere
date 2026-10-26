const removed = new Set(['admin','api','backend','media','register','registration','login','auth','account']);
export const securityHeaders = {
  'X-Lumiere-Mode':'static-demo',
  'X-Content-Type-Options':'nosniff',
  'Referrer-Policy':'no-referrer',
  'Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; media-src 'self'; connect-src 'self'; form-action 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'"
};

/** Reject before reading body, cookies, credentials or database configuration. */
export function demoResponse(request) {
  if (!['GET','HEAD'].includes(request.method))
    return new Response(null,{status:405,headers:{...securityHeaders,'Allow':'GET, HEAD','Cache-Control':'no-store'}});
  let path;
  try { path = decodeURIComponent(new URL(request.url).pathname).replaceAll('\\','/'); }
  catch { return new Response(null,{status:400,headers:securityHeaders}); }
  if (path.split('/').some(segment=>removed.has(segment.split('.')[0].toLowerCase())))
    return new Response(null,{status:410,headers:{...securityHeaders,'Cache-Control':'no-store'}});
  return null;
}

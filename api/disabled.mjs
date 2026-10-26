// A tombstone for legacy serverless URLs. No proxy, body parsing or database.
const disabled = () => new Response(null,{status:410,headers:{'Cache-Control':'no-store','X-Lumiere-Mode':'static-demo'}});
export const GET=disabled;
export const HEAD=disabled;
export const POST=disabled;
export const PUT=disabled;
export const PATCH=disabled;
export const DELETE=disabled;

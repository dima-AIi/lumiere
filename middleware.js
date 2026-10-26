import { next } from '@vercel/functions';
import { demoResponse, securityHeaders } from './demo-policy.mjs';

export default function middleware(request) {
  return demoResponse(request) ?? next({headers:securityHeaders});
}
export const config = { matcher:'/:path*' };

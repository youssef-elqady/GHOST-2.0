import test from 'node:test'
import assert from 'node:assert/strict'

type Call = { url: string; auth: string | null; credentials?: string }
const calls: Call[] = []
let mode: 'refresh-ok' | 'refresh-fail' = 'refresh-ok'
const auth = (t: string) => ({ status: 'authenticated', tokenType: 'Bearer', accessToken: t, expiresAtUtc: '2099-01-01T00:00:00Z', user: { id: '1', username: 'a', fullName: 'A', roles: ['Admin'] } })

;(globalThis as any).window = { dispatchEvent() {} }
globalThis.fetch = (async (url: string, init: RequestInit = {}) => {
  const headers = new Headers(init.headers)
  calls.push({ url: String(url), auth: headers.get('Authorization'), credentials: init.credentials })
  if (String(url).endsWith('/api/auth/refresh')) {
    await new Promise((r) => setTimeout(r, 20))
    return mode === 'refresh-ok' ? Response.json(auth('NEW')) : new Response(null, { status: 401 })
  }
  return headers.get('Authorization') === 'Bearer NEW' ? Response.json([{ id: 'd' }]) : new Response(null, { status: 401 })
}) as typeof fetch

const api = await import('../lib/ghost-api.ts')

test('401 refreshes once, retries with the new token, and notifies the provider', async () => {
  calls.length = 0
  let notified = ''
  api.configureApiAuth({ token: 'OLD', onRefreshed: (a) => { notified = a.accessToken } })
  const devices = await api.getDevices('OLD')
  assert.equal(devices.length, 1)
  assert.equal(notified, 'NEW')
  assert.deepEqual(calls.map((c) => c.auth), ['Bearer OLD', null, 'Bearer NEW'])
  assert.ok(calls.every((c) => c.credentials === 'include'))
})

test('parallel 401s share one refresh call', async () => {
  calls.length = 0
  api.configureApiAuth({ token: 'OLD' })
  await Promise.all([api.getDevices('OLD'), api.getDevices('OLD'), api.getDevices('OLD')])
  assert.equal(calls.filter((c) => c.url.endsWith('/api/auth/refresh')).length, 1)
})

test('failed refresh does not retry in a loop and surfaces an error', async () => {
  calls.length = 0
  mode = 'refresh-fail'
  api.configureApiAuth({ token: 'OLD' })
  await assert.rejects(() => api.getDevices('OLD'))
  assert.equal(calls.length, 2)
})

test('login 401 never triggers a refresh', async () => {
  calls.length = 0
  await assert.rejects(() => api.login('a', 'b'))
  assert.equal(calls.filter((c) => c.url.endsWith('/api/auth/refresh')).length, 0)
})

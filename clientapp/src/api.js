const base = '/api/v1'
async function req(path, opts = {}) {
  const res = await fetch(base + path, {
    headers: { 'Content-Type': 'application/json' }, credentials: 'same-origin',
    ...opts, body: opts.body ? JSON.stringify(opts.body) : undefined
  })
  const text = await res.text(); const data = text ? JSON.parse(text) : null
  if (!res.ok) throw new Error(data?.error || `Lỗi ${res.status}`)
  return { data, cache: res.headers.get('X-Cache') }
}
export const api = {
  dashboard: () => req('/dashboard'),
  users: () => req('/users'),
  createUser: (b) => req('/users', { method: 'POST', body: b }),
  toggleUser: (id) => req(`/users/${id}/toggle`, { method: 'POST' }),
  clients: () => req('/clients'),
  createClient: (b) => req('/clients', { method: 'POST', body: b }),
  toggleClient: (id) => req(`/clients/${id}/toggle`, { method: 'POST' }),
  oidc: () => req('/oidc-info')
}
export const fmtDate = (s) => s ? new Date(s).toLocaleDateString('vi-VN') : '—'

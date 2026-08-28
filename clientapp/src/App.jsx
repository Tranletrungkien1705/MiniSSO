import React, { useEffect, useState } from 'react'
import { Routes, Route, NavLink, Outlet } from 'react-router-dom'
import { api, fmtDate } from './api'

function Badge({ text, css }) { return <span className={`badge ${css || 'secondary'}`}>{text}</span> }
function Flash({ msg }) { return msg ? <div className={`flash ${msg.ok ? 'ok' : 'err'}`}>{msg.text}</div> : null }
function Modal({ title, onClose, wide, children }) {
  return (
    <div className="modal-bg" onClick={onClose}>
      <div className="modal" style={wide ? { maxWidth: 640 } : undefined} onClick={e => e.stopPropagation()}>
        <div className="row" style={{ marginBottom: 12 }}><h2 style={{ flex: 1, margin: 0 }}>{title}</h2>
          <button className="btn gray sm" style={{ flex: 'none' }} onClick={onClose}>Đóng</button></div>{children}
      </div>
    </div>
  )
}
function Field({ label, children }) { return <div style={{ flex: 1 }}><label>{label}</label>{children}</div> }

function Layout() {
  return (
    <>
      <nav className="nav"><span className="brand">🔐 MiniSSO</span>
        <NavLink to="/" end>Tổng quan</NavLink><NavLink to="/users">Người dùng</NavLink>
        <NavLink to="/clients">Client OAuth</NavLink><NavLink to="/oidc">Tích hợp OIDC</NavLink></nav>
      <div className="wrap"><Outlet /></div>
    </>
  )
}

function Dashboard() {
  const [d, setD] = useState(null); const [cache, setCache] = useState('')
  useEffect(() => { api.dashboard().then(r => { setD(r.data); setCache(r.cache) }) }, [])
  if (!d) return <p className="muted">Đang tải…</p>
  return (
    <>
      <h1>Tổng quan IdP {cache && <span className="pill">cache: {cache}</span>}</h1>
      <div className="grid kpis" style={{ marginBottom: 16 }}>
        <div className="kpi"><div className="v">{d.users}</div><div className="l">Người dùng ({d.activeUsers} hoạt động)</div></div>
        <div className="kpi"><div className="v">{d.clients}</div><div className="l">Client OAuth</div></div>
        <div className="kpi"><div className="v">{d.activeTokens}</div><div className="l">Refresh token đang dùng</div></div>
      </div>
      <div className="card"><h2>Issuer</h2><p style={{ fontFamily: 'monospace' }}>{d.issuer}</p>
        <p className="muted">IdP OAuth2/OIDC thay iNOS — JWT ký RSA-256, hỗ trợ authorization_code+PKCE, password, client_credentials, refresh_token.</p></div>
    </>
  )
}

function Users() {
  const [rows, setRows] = useState([]); const [show, setShow] = useState(false); const [msg, setMsg] = useState(null)
  const load = () => api.users().then(r => setRows(r.data))
  useEffect(() => { load() }, [])
  const toggle = async (id) => { try { await api.toggleUser(id); load() } catch (e) { setMsg({ ok: false, text: e.message }) } }
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 1 }}>Người dùng</h1><button className="btn sm" style={{ flex: 'none' }} onClick={() => setShow(true)}>+ Thêm user</button></div>
      <Flash msg={msg} />
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Email</th><th>Họ tên</th><th>Vai trò</th><th>Tenant</th><th>Trạng thái</th><th></th></tr></thead>
          <tbody>{rows.map(u => (<tr key={u.id}><td>{u.email}</td><td>{u.fullName}</td><td>{u.roles.map((r, i) => <span key={i} className="pill" style={{ marginRight: 3 }}>{r}</span>)}</td><td>{u.tenant || '—'}</td>
            <td><Badge text={u.isActive ? 'Hoạt động' : 'Khóa'} css={u.isActive ? 'success' : 'dark'} /></td>
            <td className="right"><button className="btn gray sm" style={{ flex: 'none' }} onClick={() => toggle(u.id)}>{u.isActive ? 'Khóa' : 'Mở'}</button></td></tr>))}
            {rows.length === 0 && <tr><td colSpan={6} className="muted" style={{ padding: 20 }}>Chưa có user.</td></tr>}</tbody></table>
      </div>
      {show && <UserForm onClose={() => setShow(false)} onSaved={() => { setShow(false); load() }} />}
    </>
  )
}

function UserForm({ onClose, onSaved }) {
  const [f, setF] = useState({ email: '', fullName: '', password: '', roles: '', tenant: '' }); const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => { try { if (!f.email || !f.password) { setErr('Cần email + mật khẩu'); return } await api.createUser(f); onSaved() } catch (e) { setErr(e.message) } }
  return (
    <Modal title="Thêm người dùng" onClose={onClose}>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <div className="row"><Field label="Email *"><input value={f.email} onChange={e => up('email', e.target.value)} /></Field>
        <Field label="Họ tên"><input value={f.fullName} onChange={e => up('fullName', e.target.value)} /></Field></div>
      <Field label="Mật khẩu *"><input type="password" value={f.password} onChange={e => up('password', e.target.value)} /></Field>
      <div className="row"><Field label="Vai trò (csv, VD Admin,Sales)"><input value={f.roles} onChange={e => up('roles', e.target.value)} /></Field>
        <Field label="Tenant/Đại lý"><input value={f.tenant} onChange={e => up('tenant', e.target.value)} /></Field></div>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Tạo user</button></div>
    </Modal>
  )
}

function Clients() {
  const [rows, setRows] = useState([]); const [show, setShow] = useState(false); const [msg, setMsg] = useState(null)
  const load = () => api.clients().then(r => setRows(r.data))
  useEffect(() => { load() }, [])
  const toggle = async (id) => { try { await api.toggleClient(id); load() } catch (e) { setMsg({ ok: false, text: e.message }) } }
  return (
    <>
      <div className="toolbar"><h1 style={{ margin: 0, flex: 1 }}>Client OAuth</h1><button className="btn sm" style={{ flex: 'none' }} onClick={() => setShow(true)}>+ Thêm client</button></div>
      <Flash msg={msg} />
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><thead><tr><th>Client ID</th><th>Tên</th><th>Loại</th><th>Grants</th><th>Trạng thái</th><th></th></tr></thead>
          <tbody>{rows.map(c => (<tr key={c.id}><td style={{ fontFamily: 'monospace' }}>{c.clientId}</td><td>{c.name}</td>
            <td>{c.isPublic ? <span className="pill">Public (PKCE)</span> : <span className="pill">Confidential</span>}</td>
            <td className="muted" style={{ fontSize: 12 }}>{c.grants.join(', ')}</td>
            <td><Badge text={c.isActive ? 'Bật' : 'Tắt'} css={c.isActive ? 'success' : 'dark'} /></td>
            <td className="right"><button className="btn gray sm" style={{ flex: 'none' }} onClick={() => toggle(c.id)}>{c.isActive ? 'Tắt' : 'Bật'}</button></td></tr>))}
            {rows.length === 0 && <tr><td colSpan={6} className="muted" style={{ padding: 20 }}>Chưa có client.</td></tr>}</tbody></table>
      </div>
      {show && <ClientForm onClose={() => setShow(false)} onSaved={() => { setShow(false); load() }} />}
    </>
  )
}

function ClientForm({ onClose, onSaved }) {
  const [f, setF] = useState({ clientId: '', name: '', redirectUris: '', grants: 'authorization_code,refresh_token', scopes: 'openid,profile,email', secret: '', requirePkce: true }); const [err, setErr] = useState('')
  const up = (k, v) => setF({ ...f, [k]: v })
  const save = async () => { try { if (!f.clientId) { setErr('Cần Client ID'); return } await api.createClient(f); onSaved() } catch (e) { setErr(e.message) } }
  return (
    <Modal title="Thêm client OAuth" onClose={onClose} wide>
      {err && <Flash msg={{ ok: false, text: err }} />}
      <div className="row"><Field label="Client ID *"><input value={f.clientId} onChange={e => up('clientId', e.target.value)} /></Field>
        <Field label="Tên"><input value={f.name} onChange={e => up('name', e.target.value)} /></Field></div>
      <Field label="Redirect URIs (csv)"><input value={f.redirectUris} onChange={e => up('redirectUris', e.target.value)} /></Field>
      <div className="row"><Field label="Grants (csv)"><input value={f.grants} onChange={e => up('grants', e.target.value)} /></Field>
        <Field label="Scopes (csv)"><input value={f.scopes} onChange={e => up('scopes', e.target.value)} /></Field></div>
      <div className="row"><Field label="Client secret (để trống = public/PKCE)"><input value={f.secret} onChange={e => up('secret', e.target.value)} /></Field></div>
      <label style={{ display: 'flex', gap: 6, alignItems: 'center', marginTop: 8 }}><input type="checkbox" style={{ width: 'auto' }} checked={f.requirePkce} onChange={e => up('requirePkce', e.target.checked)} /> Yêu cầu PKCE</label>
      <div style={{ marginTop: 16 }}><button className="btn" onClick={save}>Tạo client</button></div>
    </Modal>
  )
}

function Oidc() {
  const [o, setO] = useState(null)
  useEffect(() => { api.oidc().then(r => setO(r.data)) }, [])
  if (!o) return <p className="muted">Đang tải…</p>
  const rows = [['Issuer', o.issuer], ['Discovery', o.discovery], ['JWKS', o.jwks], ['Token endpoint', o.token], ['Authorize', o.authorize], ['UserInfo', o.userinfo]]
  return (
    <>
      <h1>Tích hợp OIDC</h1>
      <p className="muted">Các app trong hệ sinh thái (MiniMobile, MiniDMS…) dùng các endpoint sau để xác thực JWT (RS256):</p>
      <div className="card" style={{ padding: 0, overflow: 'auto' }}>
        <table><tbody>{rows.map(([k, v], i) => <tr key={i}><td className="muted" style={{ width: 160 }}>{k}</td><td style={{ fontFamily: 'monospace', fontSize: 12, wordBreak: 'break-all' }}>{v}</td></tr>)}</tbody></table>
      </div>
      <div className="card"><b>Ví dụ password grant:</b>
        <pre style={{ background: '#f8fafc', padding: 12, borderRadius: 8, overflow: 'auto', fontSize: 12 }}>{`POST ${o.token}
grant_type=password&client_id=...&client_secret=...&username=...&password=...&scope=openid profile`}</pre>
      </div>
    </>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<Dashboard />} />
        <Route path="users" element={<Users />} />
        <Route path="clients" element={<Clients />} />
        <Route path="oidc" element={<Oidc />} />
      </Route>
    </Routes>
  )
}

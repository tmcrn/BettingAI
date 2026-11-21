'use strict';

const screens = [...document.querySelectorAll('.screen')];
const tabs = [...document.querySelectorAll('.tabbar a')];
const sheet = document.getElementById('sheet');
const burger = document.getElementById('burger');

// --- Navigation par hash : #accueil, #sorties, #galerie, #membres, etc.
function show() {
  const id = (location.hash || '#accueil').slice(1);
  const target = screens.find(s => s.id === id) || screens[0];
  screens.forEach(s => { s.hidden = s !== target; });
  tabs.forEach(a => {
    if (a.dataset.tab === target.id) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
  });
  document.title = (target.dataset.title ? target.dataset.title + ' · ' : '') + 'Béruges Moto Club';
  closeSheet();
  window.scrollTo(0, 0);
}
window.addEventListener('hashchange', show);

// --- Menu
function closeSheet() { sheet.hidden = true; burger.setAttribute('aria-expanded', 'false'); }
burger.addEventListener('click', () => {
  const open = sheet.hidden;
  sheet.hidden = !open;
  burger.setAttribute('aria-expanded', String(open));
});
document.addEventListener('keydown', e => { if (e.key === 'Escape') closeSheet(); });

// --- Présence aux sorties (mémorisée sur le téléphone)
const store = {
  get() { try { return JSON.parse(localStorage.getItem('bmc-presences') || '{}'); } catch { return {}; } },
  set(v) { try { localStorage.setItem('bmc-presences', JSON.stringify(v)); } catch { /* stockage indisponible */ } }
};
function paintRsvp() {
  const state = store.get();
  document.querySelectorAll('.event').forEach(ev => {
    const btn = ev.querySelector('.rsvp');
    const on = !!state[ev.dataset.event];
    btn.setAttribute('aria-pressed', String(on));
    btn.textContent = on ? (btn.dataset.on || 'Je viens ✓') : (btn.dataset.off || 'Je viens');
  });
}
document.addEventListener('click', e => {
  const btn = e.target.closest('.rsvp');
  if (!btn) return;
  const key = btn.closest('.event').dataset.event;
  const state = store.get();
  state[key] = !state[key];
  store.set(state);
  paintRsvp();
});

// --- Annuaire (exemples)
const people = [
  { nom: 'Prénom N.', role: 'Président', tel: '0600000001' },
  { nom: 'Prénom N.', role: 'Secrétaire', tel: '0600000002' },
  { nom: 'Prénom N.', role: 'Trésorier', tel: '0600000003' },
  { nom: 'Prénom N.', role: 'Membre', tel: '0600000004' },
  { nom: 'Prénom N.', role: 'Membre', tel: '0600000005' },
  { nom: 'Prénom N.', role: 'Membre', tel: '0600000006' }
];
const phoneIcon = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8.1 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z"/></svg>';
const list = document.getElementById('people');
function renderPeople(q = '') {
  const needle = q.trim().toLowerCase();
  const rows = people.filter(p => !needle || (p.nom + ' ' + p.role).toLowerCase().includes(needle));
  list.replaceChildren();
  if (!rows.length) {
    const li = document.createElement('li'); li.className = 'empty'; li.textContent = 'Aucun membre ne correspond. Essaie un autre nom.'; list.append(li); return;
  }
  rows.forEach(p => {
    const li = document.createElement('li');
    const av = document.createElement('span'); av.className = 'avatar'; av.textContent = p.nom.split(' ').map(w => w[0]).join('').slice(0, 2);
    const who = document.createElement('div'); who.className = 'who';
    const b = document.createElement('b'); b.textContent = p.nom;
    const s = document.createElement('span'); s.textContent = p.role;
    who.append(b, s);
    const a = document.createElement('a'); a.className = 'call'; a.href = 'tel:' + p.tel; a.setAttribute('aria-label', 'Appeler ' + p.nom + ' (' + p.role + ')'); a.innerHTML = phoneIcon;
    li.append(av, who, a); list.append(li);
  });
}
document.getElementById('q').addEventListener('input', e => renderPeople(e.target.value));

// --- Sous-onglets membres
const segTabs = [...document.querySelectorAll('.seg [role="tab"]')];
segTabs.forEach(t => t.addEventListener('click', () => {
  segTabs.forEach(x => {
    const on = x === t;
    x.setAttribute('aria-selected', String(on));
    document.getElementById(x.getAttribute('aria-controls')).hidden = !on;
  });
}));

// --- Formulaire d'adhésion (maquette : pas d'envoi)
document.getElementById('form').addEventListener('submit', e => {
  e.preventDefault();
  const f = e.currentTarget;
  if (!f.checkValidity()) { f.reportValidity(); return; }
  document.getElementById('ok').hidden = false;
});

renderPeople();
paintRsvp();
show();

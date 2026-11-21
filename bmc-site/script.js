'use strict';
/* =========================================================
   BMC : application mobile du Béruges Moto Club
   Données : couche "db" enregistrée dans le navigateur (localStorage).
   Pour la mise en ligne, seule cette couche (load/save) est à
   remplacer par un vrai serveur ; les écrans ne changent pas.
   ========================================================= */

/* ---------- Données ---------- */
const KEY = 'bmc-data-v1', SKEY = 'bmc-session';
const uid = () => Math.random().toString(36).slice(2, 9);
const MOIS = ['janv.', 'févr.', 'mars', 'avr.', 'mai', 'juin', 'juil.', 'août', 'sept.', 'oct.', 'nov.', 'déc.'];
const ROLES = ['Président', 'Secrétaire', 'Trésorier', 'Membre'];
const BUREAU = ['Président', 'Secrétaire', 'Trésorier'];

function seed() {
  return {
    settings: {
      youtube: '', braderieDate: 'À confirmer', braderieLieu: 'Béruges',
      // Contacts publiés par le club sur Intramuros (fiche « Béruges Moto Club (BMC) »). Rôles non précisés dans cette source.
      email: 'berugesmotoclub86@gmail.com',
      contacts: [{ nom: 'Nicolas Ribardiere', tel: '06 74 93 08 62' }, { nom: 'Guy Périssat', tel: '06 37 96 43 03' }]
    },
    members: [
      { id: 'm1', nom: 'Prénom Président', role: 'Président', tel: '0600000001', email: 'president@bmc.test', numero: '0001', expire: '2027-12-31', code: '1111', parrainId: null },
      { id: 'm2', nom: 'Prénom Secrétaire', role: 'Secrétaire', tel: '0600000002', email: 'secretaire@bmc.test', numero: '0002', expire: '2027-12-31', code: '2222', parrainId: null },
      { id: 'm3', nom: 'Prénom Trésorier', role: 'Trésorier', tel: '0600000003', email: 'tresorier@bmc.test', numero: '0003', expire: '2027-12-31', code: '3333', parrainId: null },
      { id: 'm4', nom: 'Prénom Membre', role: 'Membre', tel: '0600000004', email: 'membre@bmc.test', numero: '0042', expire: '2027-12-31', code: '4444', parrainId: 'm1' },
      { id: 'm5', nom: 'Autre Membre', role: 'Membre', tel: '0600000005', email: 'autre@bmc.test', numero: '0043', expire: '2026-06-30', code: '5555', parrainId: 'm4' }
    ],
    events: [
      { id: 'e1', date: '2027-03-14', titre: 'Sortie de printemps', desc: 'Boucle d’une journée, pause déjeuner en route. Départ 9 h 30, devant la mairie.', inscription: false },
      { id: 'e2', date: '2027-05-08', titre: 'Balade du pont de mai', desc: 'Campagne, arrêt photo et pique-nique.', inscription: false },
      { id: 'e3', date: '2027-06-20', titre: 'Journée circuit', desc: 'Pour rouler plus vite, en sécurité.', inscription: true }
    ],
    rsvp: { e1: ['m1', 'm4'] },
    pointage: {},
    communiques: [
      { id: 'c1', date: '2026-11-12', titre: 'Assemblée générale', texte: 'L’assemblée générale aura lieu en début d’année. La convocation et l’ordre du jour seront publiés ici.' },
      { id: 'c2', date: '2026-10-28', titre: 'Inventaire du matériel de course', texte: 'Merci de rapporter le matériel prêté avant fin novembre.' }
    ],
    // Une fiche par événement : ce qui a été acheté et sorti, pour préparer l'édition suivante. Quantités d'exemple.
    fiches: [{
      id: 'f1', titre: 'Journée huîtres', date: '2026-10-18', items: [
        { id: 'fi1', nom: 'Huîtres', categorie: 'Nourriture', qte: 30, unite: 'douzaines', prix: '' },
        { id: 'fi2', nom: 'Charcuterie', categorie: 'Nourriture', qte: 8, unite: 'kg', prix: '' },
        { id: 'fi3', nom: 'Pain', categorie: 'Nourriture', qte: 25, unite: 'baguettes', prix: '' },
        { id: 'fi4', nom: 'Beurre', categorie: 'Nourriture', qte: 3, unite: 'kg', prix: '' },
        { id: 'fi5', nom: 'Citrons', categorie: 'Nourriture', qte: 6, unite: 'kg', prix: '' },
        { id: 'fi6', nom: 'Vin blanc', categorie: 'Boissons', qte: 36, unite: 'bouteilles', prix: '' },
        { id: 'fi7', nom: 'Bières', categorie: 'Boissons', qte: 8, unite: 'caisses', prix: '' },
        { id: 'fi8', nom: 'Boissons sans alcool', categorie: 'Boissons', qte: 6, unite: 'caisses', prix: '' },
        { id: 'fi9', nom: 'Tivolis', categorie: 'Matériel', qte: 4, unite: 'pièces', prix: '' },
        { id: 'fi10', nom: 'Tables', categorie: 'Matériel', qte: 12, unite: 'pièces', prix: '' },
        { id: 'fi11', nom: 'Vaisselle', categorie: 'Matériel', qte: 150, unite: 'pièces', prix: '' },
        { id: 'fi12', nom: 'Gobelets', categorie: 'Matériel', qte: 300, unite: 'pièces', prix: '' },
        { id: 'fi13', nom: 'Sacs poubelle', categorie: 'Autre', qte: 2, unite: 'rouleaux', prix: '' }
      ]
    }],
    partenaires: [
      { id: 'p1', nom: 'Garage du Bourg', remise: '−10 %' },
      { id: 'p2', nom: 'Équipement Moto Sud', remise: '−15 %' },
      { id: 'p3', nom: 'Café de la Place', remise: '1 café offert' }
    ],
    albums: [
      { id: 'a1', titre: 'Braderie 2026', annee: 2026, couleur: 1, photos: [] },
      { id: 'a2', titre: 'Sortie de printemps 2026', annee: 2026, couleur: 2, photos: [] },
      { id: 'a3', titre: 'Soirée du club 2025', annee: 2025, couleur: 3, photos: [] }
    ],
    demandes: []
  };
}

let db, sessionId = null, persistent = true;
function load() {
  try { db = JSON.parse(localStorage.getItem(KEY)); } catch { db = null; persistent = false; }
  if (!db || typeof db !== 'object' || !Array.isArray(db.members)) db = seed();
  const base = seed();
  for (const k of Object.keys(base)) if (db[k] == null) db[k] = base[k];          // migration : clés ajoutées plus tard
  db.settings = { ...base.settings, ...db.settings };
  try { sessionId = localStorage.getItem(SKEY); } catch { /* session non conservée */ }
}
function save() {
  try { localStorage.setItem(KEY, JSON.stringify(db)); return true; }
  catch { toast('Enregistrement impossible : stockage plein ou indisponible. Retire des photos.'); return false; }
}
function setSession(id) {
  sessionId = id;
  try { id ? localStorage.setItem(SKEY, id) : localStorage.removeItem(SKEY); } catch { /* ignore */ }
}
/* Photos : IndexedDB (hors du JSON principal, qui reste léger). Repli en mémoire si indisponible. */
const photoStore = (() => {
  const mem = new Map(); let opened = null;
  const open = () => opened || (opened = new Promise(res => {
    try { const r = indexedDB.open('bmc-photos', 1); r.onupgradeneeded = () => r.result.createObjectStore('p'); r.onsuccess = () => res(r.result); r.onerror = () => res(null); r.onblocked = () => res(null); }
    catch { res(null); }
  }));
  const run = async (mode, fn) => {
    const d = await open(); if (!d) return null;
    return new Promise(res => { try { const t = d.transaction('p', mode), req = fn(t.objectStore('p')); t.oncomplete = () => res(req && 'result' in req ? req.result : true); t.onerror = t.onabort = () => res(null); } catch { res(null); } });
  };
  return {
    async put(id, data) { mem.set(id, data); await run('readwrite', s => s.put(data, id)); },
    async get(id) { if (mem.has(id)) return mem.get(id); const r = await run('readonly', s => s.get(id)); return typeof r === 'string' ? r : null; },
    async del(id) { mem.delete(id); await run('readwrite', s => s.delete(id)); },
    async clear() { mem.clear(); await run('readwrite', s => s.clear()); }
  };
})();
const photoSrc = entry => entry.startsWith('data:') ? Promise.resolve(entry) : photoStore.get(entry);   // anciennes photos : data: en ligne
const dropPhotos = album => album.photos.forEach(e => { if (!e.startsWith('data:')) photoStore.del(e); });

const me = () => db.members.find(m => m.id === sessionId) || null;
const isAdmin = () => { const m = me(); return !!m && BUREAU.includes(m.role); };
const byId = (list, id) => list.find(x => x.id === id);

/* ---------- Outils ---------- */
const SVG_TAGS = new Set(['svg', 'path']);
function h(tag, attrs, ...kids) {
  const e = SVG_TAGS.has(tag) ? document.createElementNS('http://www.w3.org/2000/svg', tag) : document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null || v === false) continue;
    if (k === 'class') e.setAttribute('class', v);
    else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else if (k === 'value') e.value = v;
    else e.setAttribute(k, v === true ? '' : v);
  }
  for (const c of kids.flat(Infinity)) { if (c == null || c === false) continue; e.append(c.nodeType ? c : document.createTextNode(String(c))); }
  return e;
}
const $ = (s, r = document) => r.querySelector(s);
const isoLocal = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
const today = () => isoLocal(new Date());
const dparts = iso => { const [y, m, d] = iso.split('-').map(Number); return { y, d: String(d).padStart(2, '0'), m: MOIS[m - 1] }; };
const fmtLong = iso => { const p = dparts(iso); return `${Number(p.d)} ${p.m} ${p.y}`; };
const telHref = t => 'tel:' + String(t).replace(/[^0-9+]/g, '');
const initials = n => n.split(/\s+/).map(w => w[0]).join('').slice(0, 2).toUpperCase();
const isValid = m => m.expire >= today();
const nextNumero = () => String(Math.max(0, ...db.members.map(m => parseInt(m.numero, 10) || 0)) + 1).padStart(4, '0');
const addYear = iso => {
  const [y, m, d] = iso.split('-'), ny = (iso < today() ? Number(today().slice(0, 4)) : Number(y)) + 1;
  const leap = (ny % 4 === 0 && ny % 100 !== 0) || ny % 400 === 0;
  return `${ny}-${m}-${m === '02' && d === '29' && !leap ? '28' : d}`;
};
const norm = t => String(t || '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
const emailTaken = (email, exceptId) => db.members.some(m => m.id !== exceptId && norm(m.email) === norm(email));
const bureauCount = () => db.members.filter(m => BUREAU.includes(m.role)).length;
let toastTimer;
function toast(msg) { const t = $('#toast'); $('#live').textContent = ''; setTimeout(() => { $('#live').textContent = msg; }, 50); t.textContent = msg; t.hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => { t.hidden = true; }, 3500); }

function askConfirm(message, okLabel = 'Supprimer') {
  return new Promise(resolve => {
    const d = h('dialog', { class: 'confirm', 'aria-label': message }, h('p', {}, message),
      h('div', { class: 'acts' }, btn('Annuler', () => d.close('no'), 'btn-line btn-sm'), btn(okLabel, () => d.close('yes'), 'btn-danger btn-sm')));
    d.addEventListener('close', () => { d.remove(); resolve(d.returnValue === 'yes'); });
    document.body.append(d); d.showModal();
  });
}
function field(label, control, hint) { return h('label', {}, h('span', { class: 'lbl' }, label, hint ? h('span', { class: 'opt' }, ' ' + hint) : null), control); }
const input = (name, o = {}) => h('input', { name, type: o.type || 'text', required: o.req, autocomplete: o.auto || 'off', placeholder: o.ph || ' ', value: o.value, inputmode: o.mode, min: o.min, max: o.max, pattern: o.pattern, title: o.title });
const textarea = (name, o = {}) => h('textarea', { name, rows: o.rows || 4, required: o.req, placeholder: o.ph || ' ' });
const btn = (label, onclick, cls = '', type = 'button') => h('button', { type, class: 'btn ' + cls, onclick }, label);
const pageTitle = (t, sub) => [h('h1', { class: 'page-title' }, t), sub ? h('p', { class: 'muted' }, sub) : null];

function form(children, onSubmit, submitLabel = 'Enregistrer') {
  const f = h('form', { class: 'form', novalidate: true }, children, btn(submitLabel, null, 'btn-full', 'submit'));
  f.addEventListener('submit', e => {
    e.preventDefault();
    if (!f.checkValidity()) { f.classList.add('shake'); f.addEventListener('animationend', () => f.classList.remove('shake'), { once: true }); f.reportValidity(); return; }
    const data = Object.fromEntries(new FormData(f).entries());
    if (onSubmit(data) !== false) f.reset();
  });
  return f;
}

/* ---------- Écrans publics ---------- */
function nextEvent() {
  const sorted = [...db.events].sort((a, b) => a.date.localeCompare(b.date));
  return sorted.find(e => e.date >= today()) || null;
}
function eventCard(ev) {
  const p = dparts(ev.date), m = me();
  const going = (db.rsvp[ev.id] || []).length;
  const mine = m && (db.rsvp[ev.id] || []).includes(m.id);
  const on = ev.inscription ? 'Inscrit' : 'Je viens ✓', off = ev.inscription ? 'Je m’inscris' : 'Je viens';
  const action = m
    ? h('button', { type: 'button', class: 'btn btn-full' + (popId === ev.id && mine ? ' pop' : ''), 'aria-pressed': String(!!mine), onclick: () => toggleRsvp(ev.id) }, mine ? on : off)
    : h('a', { class: 'btn btn-line btn-full', href: '#connexion' }, 'Se connecter pour répondre');
  return h('article', { class: 'event' },
    h('time', { datetime: ev.date }, h('b', {}, p.d), p.m),
    h('div', { class: 'event-body' }, h('h3', {}, ev.titre), h('p', {}, ev.desc),
      ev.lien ? h('a', { class: 'route', href: ev.lien, target: '_blank', rel: 'noopener' }, 'Itinéraire sur 68°') : null, going ? h('p', { class: 'going' }, going + (going > 1 ? ' membres viennent' : ' membre vient')) : null),
    action);
}
function toggleRsvp(id) {
  const m = me(); if (!m) return;
  const list = new Set(db.rsvp[id] || []);
  list.has(m.id) ? list.delete(m.id) : list.add(m.id);
  db.rsvp[id] = [...list]; popId = id; save(); render();
}
function albumTile(a) {
  return h('a', { class: 'album a' + ((a.couleur || 1) % 4 || 1), href: '#album-' + a.id },
    h('span', { class: 'album-title' }, a.titre),
    h('span', { class: 'album-meta' }, a.photos.length ? a.photos.length + (a.photos.length > 1 ? ' photos' : ' photo') : 'Aucune photo pour l’instant'));
}

function contactsBlock() {
  const st = db.settings;
  return h('div', { class: 'contacts' }, h('h3', {}, 'Contacts du club'),
    h('ul', { class: 'bureau' }, st.contacts.map(c => h('li', {}, h('b', {}, c.nom), h('a', { href: telHref(c.tel) }, c.tel)))),
    st.email ? h('p', {}, 'E-mail : ', h('a', { href: 'mailto:' + st.email }, st.email)) : null);
}

function viewAccueil() {
  const ev = nextEvent(), alb = db.albums[0];
  return [
    h('div', { class: 'hero' },
      h('p', { class: 'hero-note' }, 'Photo du club à ajouter'),
      h('h1', {}, 'Béruges Moto Club'),
      h('p', { class: 'hero-sub' }, 'Depuis 2010, on roule ensemble.'),
      h('a', { class: 'btn', href: '#sorties' }, 'Voir la prochaine sortie')),
    h('div', { class: 'flag', 'aria-hidden': 'true' }),
    h('div', { class: 'pad' },
      h('h2', {}, 'Prochaine sortie'), ev ? eventCard(ev) : h('p', { class: 'empty' }, 'Aucune sortie prévue pour le moment. Reviens bientôt.'),
      alb ? [h('h2', {}, 'Dernier album'), albumTile(alb)] : null,
      h('h2', {}, 'Le club'),
      h('p', {}, 'Né en 2010 à Béruges, le BMC réunit des motards de tous âges. On roule le week-end, on organise la braderie et on se serre les coudes quand une moto reste au garage.'),
      contactsBlock(),
      h('a', { class: 'btn btn-line btn-full', href: '#rejoindre' }, 'Rejoindre le club'),
      h('div', { class: 'links' },
        db.settings.youtube ? h('a', { class: 'btn btn-line btn-full', href: db.settings.youtube, target: '_blank', rel: 'noopener' }, 'Voir nos vidéos sur YouTube') : null,
        h('a', { class: 'btn btn-line btn-full', href: '#securite' }, 'Rouler en sécurité')))
  ];
}

function viewSorties() {
  const list = [...db.events].sort((a, b) => a.date.localeCompare(b.date)).filter(e => e.date >= today());
  return h('div', { class: 'pad' }, pageTitle('Prochaines sorties', 'Dis-nous si tu viens, ça aide à s’organiser.'),
    list.length ? list.map(eventCard) : h('p', { class: 'empty' }, 'Aucune sortie prévue pour le moment. Reviens bientôt.'));
}

function viewGalerie() {
  const albums = [...db.albums].sort((a, b) => b.annee - a.annee);
  return h('div', { class: 'pad' }, pageTitle('Galerie', 'Les albums du club, classés par année.'),
    albums.length ? albums.map(a => albumTile(a)) : h('p', { class: 'empty' }, 'Aucun album pour le moment.'));
}

let lightbox;
function viewAlbum(id) {
  const a = byId(db.albums, id);
  if (!a) return h('div', { class: 'pad' }, h('p', { class: 'empty' }, 'Cet album n’existe pas. '), h('a', { class: 'btn', href: '#galerie' }, 'Retour à la galerie'));
  const grid = a.photos.length
    ? h('div', { class: 'photos' }, a.photos.map((entry, i) => {
      const img = h('img', { alt: `${a.titre}, photo ${i + 1}`, loading: 'lazy' });
      const b = h('button', { type: 'button', class: 'photo', 'aria-label': `Agrandir la photo ${i + 1}`, disabled: true }, img);
      photoSrc(entry).then(src => { if (!src) { b.classList.add('missing'); b.setAttribute('aria-label', `Photo ${i + 1} indisponible`); return; } img.src = src; b.disabled = false; b.onclick = () => openPhoto(src); });
      return b;
    }))
    : h('p', { class: 'empty' }, isAdmin() ? 'Aucune photo. Ajoute les premières ci-dessous.' : 'Les photos arrivent bientôt.');
  const add = isAdmin() ? h('label', { class: 'btn btn-full upload' }, 'Ajouter des photos',
    h('input', { type: 'file', accept: 'image/*', multiple: true, class: 'sr', onchange: e => addPhotos(a, e.target.files) })) : null;
  return h('div', { class: 'pad' }, h('a', { class: 'back', href: '#galerie' }, '‹ Galerie'), pageTitle(a.titre, String(a.annee)), grid, add);
}
function openPhoto(src) {
  if (!lightbox) {
    lightbox = h('dialog', { class: 'lightbox', 'aria-label': 'Photo agrandie' }, h('img', { alt: '' }), h('button', { type: 'button', class: 'btn', onclick: () => lightbox.close() }, 'Fermer'));
    document.body.append(lightbox);
    lightbox.addEventListener('click', e => { if (e.target === lightbox) lightbox.close(); });
  }
  $('img', lightbox).src = src; lightbox.showModal();
}
function shrink(file, max = 900) {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file), img = new Image();
    img.onload = () => {
      const r = Math.min(1, max / Math.max(img.width, img.height)), c = document.createElement('canvas');
      c.width = Math.round(img.width * r); c.height = Math.round(img.height * r);
      c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
      URL.revokeObjectURL(url); resolve(c.toDataURL('image/jpeg', 0.7));
    };
    img.onerror = () => { URL.revokeObjectURL(url); reject(new Error('image')); };
    img.src = url;
  });
}
async function addPhotos(album, files) {
  let n = 0;
  for (const f of files) {
    if (!f.type.startsWith('image/')) continue;
    try { const id = uid(); await photoStore.put(id, await shrink(f)); album.photos.push(id); n++; } catch { toast('Une image n’a pas pu être lue.'); }
  }
  if (n && save()) toast(n + (n > 1 ? ' photos ajoutées' : ' photo ajoutée'));
  else if (n) { album.photos.splice(-n).forEach(id => photoStore.del(id)); }
  render();
}

function viewPartenaires() {
  return h('div', { class: 'pad' }, pageTitle('Partenaires et remises', 'Présente ta carte d’adhérent chez nos partenaires.'),
    dealsList(),
    h('p', { class: 'note' }, 'Un commerce veut soutenir le club ? ', h('a', { href: '#rejoindre' }, 'Écris-nous'), '.'));
}
const dealsList = () => db.partenaires.length
  ? h('ul', { class: 'deals' }, db.partenaires.map(p => h('li', {}, h('span', {}, p.nom), h('b', {}, p.remise))))
  : h('p', { class: 'empty' }, 'Aucun partenaire pour le moment.');

function viewBraderie() {
  const s = db.settings;
  return h('div', { class: 'pad' }, pageTitle('La braderie du BMC'),
    h('p', {}, 'Une fois par an, pièces, équipements, vêtements et bonnes affaires pour motards. Exposants et visiteurs sont les bienvenus.'),
    h('dl', { class: 'infos' },
      h('div', {}, h('dt', {}, 'Date'), h('dd', {}, s.braderieDate)),
      h('div', {}, h('dt', {}, 'Lieu'), h('dd', {}, s.braderieLieu)),
      h('div', {}, h('dt', {}, 'Entrée'), h('dd', {}, 'Gratuite'))),
    h('h2', {}, 'Réserver un emplacement'),
    form([field('Prénom et nom', input('nom', { req: true, auto: 'name' })), field('E-mail', input('email', { type: 'email', req: true, auto: 'email' })), field('Téléphone', input('tel', { type: 'tel', auto: 'tel' })), field('Ce que tu exposes', textarea('note', { rows: 3 }))],
      d => { db.demandes.push({ id: uid(), type: 'braderie', ...d, date: today(), statut: 'nouvelle' }); if (!save()) { db.demandes.pop(); return false; } toast('Demande envoyée au bureau.'); }, 'Envoyer ma demande'));
}

function viewRejoindre() {
  return h('div', { class: 'pad' }, pageTitle('Rejoindre le club', 'Le bureau te recontacte. Un membre t’a parlé du BMC ? Indique son nom, il te parraine.'), contactsBlock(),
    form([field('Prénom et nom', input('nom', { req: true, auto: 'name' })), field('E-mail', input('email', { type: 'email', req: true, auto: 'email' })), field('Téléphone', input('tel', { type: 'tel', req: true, auto: 'tel' })), field('Parrainé par', input('parrain', { ph: 'Nom du membre' }), '(facultatif)')],
      d => { db.demandes.push({ id: uid(), type: 'adhesion', ...d, date: today(), statut: 'nouvelle' }); if (!save()) { db.demandes.pop(); return false; } toast('Demande envoyée. Le bureau te répond rapidement.'); }, 'Envoyer ma demande'));
}

function viewSecurite() {
  return h('div', { class: 'pad' }, pageTitle('Rouler en sécurité'),
    h('p', {}, 'Le club recommande l’appli Liberty Rider : en cas de chute, elle prévient tes proches et les secours. On l’installe ensemble avant la première sortie.'),
    h('ul', { class: 'checks' }, ['Alerte automatique après une chute', 'Partage de ta position avec ton contact', 'Installation avec un ancien du club'].map(t => h('li', {}, t))));
}

/* ---------- Connexion et espace membres ---------- */
function viewConnexion() {
  if (me()) return viewMembres();
  const f = form([field('E-mail', input('email', { type: 'email', req: true, auto: 'username' })), field('Code personnel', input('code', { type: 'password', req: true, auto: 'current-password', mode: 'numeric' }))],
    d => {
      const m = db.members.find(x => x.email.toLowerCase() === d.email.trim().toLowerCase() && x.code === d.code.trim());
      if (!m) { toast('E-mail ou code incorrect. Vérifie et réessaie.'); return false; }
      setSession(m.id); location.hash = '#membres'; render(); toast('Bienvenue ' + m.nom + ' !');
    }, 'Se connecter');
  const demo = h('div', { class: 'demo' }, h('h3', {}, 'Comptes de démonstration'),
    h('p', { class: 'note' }, 'Pour tester la maquette. Ils seront supprimés à la mise en ligne.'),
    h('ul', {}, db.members.slice(0, 4).map(m => h('li', {}, h('button', { type: 'button', class: 'btn btn-line btn-sm', onclick: () => { f.email.value = m.email; f.code.value = m.code; toast('Champs remplis : touche « Se connecter ».'); } }, m.role + ' · ' + m.email + ' · ' + m.code)))));
  return h('div', { class: 'pad' }, pageTitle('Connexion', 'Espace réservé aux adhérents.'), f, demo);
}

let memTab = 'annuaire';
function viewMembres() {
  const m = me();
  if (!m) return viewConnexion();
  const tabs = [['annuaire', 'Annuaire'], ['carte', 'Ma carte'], ['infos', 'Communiqués'], ['inventaire', 'Inventaire']];
  const panels = { annuaire: panelAnnuaire, carte: panelCarte, infos: panelInfos, inventaire: panelInventaire };
  return h('div', { class: 'pad' },
    h('div', { class: 'who-bar' }, h('span', {}, 'Connecté : ', h('b', {}, m.nom), ' (', m.role, ')'), btn('Se déconnecter', () => { setSession(null); location.hash = '#accueil'; render(); toast('Déconnecté.'); }, 'btn-sm btn-line')),
    isAdmin() ? h('a', { class: 'btn btn-full', href: '#admin' }, 'Accès administrateur du bureau') : null,
    segment(tabs, memTab, k => { memTab = k; render(); }, 'mem'),
    asPanel(panels[memTab](), 'mem', memTab));
}
function segment(tabs, current, onPick, prefix, extra = '') {
  const go = k => { onPick(k); const t = document.getElementById(`${prefix}-tab-${k}`); if (t) t.focus(); };
  return h('div', { class: 'seg ' + extra, role: 'tablist' }, tabs.map(([k, l], i) => h('button', {
    type: 'button', role: 'tab', id: `${prefix}-tab-${k}`, 'aria-controls': `${prefix}-panel`, 'aria-selected': String(k === current), tabindex: k === current ? '0' : '-1',
    onclick: () => go(k),
    onkeydown: e => { const d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key]; if (d) { e.preventDefault(); go(tabs[(i + d + tabs.length) % tabs.length][0]); } }
  }, l)));
}
const asPanel = (node, prefix, current) => { node.id = `${prefix}-panel`; node.setAttribute('role', 'tabpanel'); node.setAttribute('aria-labelledby', `${prefix}-tab-${current}`); return node; };

let query = '';
function panelAnnuaire() {
  const rows = db.members.filter(x => !query || (x.nom + ' ' + x.role).toLowerCase().includes(query.toLowerCase()));
  const q = h('input', { type: 'search', placeholder: 'Rechercher un membre', 'aria-label': 'Rechercher un membre', value: query, autocomplete: 'off' });
  q.addEventListener('input', () => { query = q.value; const pos = q.selectionStart; render(); const n = $('.search input'); if (n) { n.focus(); n.setSelectionRange(pos, pos); } });
  return h('div', { role: 'tabpanel', class: 'panel' },
    h('label', { class: 'search' }, h('svg', { viewBox: '0 0 24 24', width: '20', height: '20', 'aria-hidden': 'true' }, h('path', { d: 'M21 21l-4.3-4.3M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z', fill: 'none', stroke: 'currentColor', 'stroke-width': '2', 'stroke-linecap': 'round' })), q),
    rows.length ? h('ul', { class: 'people' }, rows.map(x => h('li', {}, h('span', { class: 'avatar' }, initials(x.nom)), h('div', { class: 'who' }, h('b', {}, x.nom), h('span', {}, x.role)),
      h('a', { class: 'call', href: telHref(x.tel), 'aria-label': `Appeler ${x.nom}` }, callIcon()))))
      : h('p', { class: 'empty' }, 'Aucun membre ne correspond. Essaie un autre nom.'),
    h('p', { class: 'note' }, 'Touche le téléphone pour appeler. Ces coordonnées sont réservées aux membres du club.'));
}
const callIcon = () => h('svg', { viewBox: '0 0 24 24', 'aria-hidden': 'true' }, h('path', { d: 'M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8.1 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.8.6 2.8.7a2 2 0 0 1 1.7 2z' }));

function panelCarte() {
  const m = me(), filleuls = db.members.filter(x => x.parrainId === m.id), parrain = byId(db.members, m.parrainId);
  return h('div', { role: 'tabpanel', class: 'panel' },
    h('div', { class: 'card' }, h('span', { class: 'ring big' }, h('img', { src: 'assets/logo-bmc.jpeg', alt: '', width: 80, height: 80 })),
      h('div', {}, h('p', { class: 'card-club' }, 'Béruges Moto Club'), h('p', { class: 'card-name' }, m.nom), h('p', { class: 'card-meta' }, 'Adhérent n° ' + m.numero), h('p', { class: 'card-meta' }, 'Valable jusqu’au ' + fmtLong(m.expire)))),
    h('p', { class: 'badge ' + (isValid(m) ? 'ok' : 'ko') }, isValid(m) ? 'Cotisation à jour' : 'Cotisation à renouveler : vois avec le trésorier'),
    h('p', { class: 'muted' }, 'Montre cette carte chez nos commerçants partenaires pour avoir ta remise.'),
    h('h2', {}, 'Mes remises'), dealsList(),
    h('h2', {}, 'Parrainage'),
    h('p', {}, parrain ? 'Parrain : ' + parrain.nom + '. ' : '', filleuls.length ? 'Tu as parrainé ' + filleuls.length + (filleuls.length > 1 ? ' membres : ' : ' membre : ') + filleuls.map(f => f.nom).join(', ') + '.' : 'Tu n’as parrainé personne pour l’instant.'));
}
function panelInfos() {
  const list = [...db.communiques].sort((a, b) => b.date.localeCompare(a.date));
  return h('div', { role: 'tabpanel', class: 'panel' }, list.length ? h('ul', { class: 'news' }, list.map(c => h('li', {}, h('time', {}, fmtLong(c.date)), h('div', {}, h('h3', {}, c.titre), h('p', {}, c.texte)))))
    : h('p', { class: 'empty' }, 'Aucun communiqué pour le moment.'));
}
const CATS = ['Nourriture', 'Boissons', 'Matériel', 'Autre'];
const euro = n => new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' }).format(n);
const fichesSorted = () => [...db.fiches].sort((a, b) => b.date.localeCompare(a.date));
let ficheId = null;
const currentFiche = () => byId(db.fiches, ficheId) || fichesSorted()[0] || null;
const ficheTotal = f => f.items.reduce((sum, i) => sum + (Number(i.prix) || 0), 0);
const qtyText = i => `${i.qte}${i.unite ? ' ' + i.unite : ''}`;
function fichePicker() {
  return h('label', { class: 'pick' }, h('span', { class: 'lbl' }, 'Événement'),
    h('select', { 'aria-label': 'Choisir l’événement', onchange: e => { ficheId = e.target.value; render(); } },
      fichesSorted().map(f => h('option', { value: f.id, selected: f.id === currentFiche().id }, `${f.titre} · ${fmtLong(f.date)}`))));
}
function panelInventaire() {
  const f = currentFiche();
  if (!f) return h('div', { role: 'tabpanel', class: 'panel' }, h('p', { class: 'empty' }, 'Aucune fiche d’événement pour le moment.'));
  const groups = CATS.map(c => [c, f.items.filter(i => i.categorie === c)]).filter(([, l]) => l.length);
  const total = ficheTotal(f);
  return h('div', { role: 'tabpanel', class: 'panel' },
    h('p', { class: 'muted' }, 'Ce qu’on a acheté et sorti pour chaque événement, pour préparer le suivant.'),
    fichePicker(),
    groups.length ? groups.map(([c, l]) => h('section', { class: 'cat' }, h('h3', {}, c),
      h('ul', { class: 'stock' }, l.map(i => h('li', {}, h('b', {}, i.nom), h('span', { class: 'qty' }, qtyText(i)), i.prix !== '' && i.prix != null ? h('span', { class: 'muted' }, euro(Number(i.prix))) : null)))))
      : h('p', { class: 'empty' }, 'Cette fiche est vide. Le bureau peut y ajouter ce qu’on achète.'),
    total > 0 ? h('p', { class: 'badge ok' }, 'Total acheté : ' + euro(total)) : null,
    h('p', { class: 'note' }, 'Les quantités de la fiche de démonstration sont des exemples.'));
}

/* ---------- Administration (bureau) ---------- */
let admTab = 'demandes';
function viewAdmin() {
  if (!me()) return viewConnexion();
  if (!isAdmin()) return h('div', { class: 'pad' }, pageTitle('Accès réservé'), h('p', {}, 'Cette partie est réservée au président, à la secrétaire et au trésorier.'), h('a', { class: 'btn', href: '#membres' }, 'Retour'));
  const nb = db.demandes.filter(d => d.statut === 'nouvelle').length;
  const tabs = [['demandes', 'Demandes' + (nb ? ` (${nb})` : '')], ['membres', 'Membres'], ['pointage', 'Pointage'], ['sorties', 'Sorties'], ['infos', 'Communiqués'], ['stock', 'Inventaire'], ['partenaires', 'Partenaires'], ['albums', 'Albums'], ['reglages', 'Réglages']];
  const panels = { demandes: admDemandes, membres: admMembres, pointage: admPointage, sorties: admSorties, infos: admInfos, stock: admStock, partenaires: admPartenaires, albums: admAlbums, reglages: admReglages };
  return h('div', { class: 'pad' }, h('a', { class: 'back', href: '#membres' }, '‹ Espace membres'), pageTitle('Administration', 'Réservé au bureau : ' + me().role + '.'),
    segment(tabs, admTab, k => { admTab = k; render(); }, 'adm', 'wrap'),
    asPanel(panels[admTab](), 'adm', admTab));
}
const del = (label, fn) => btn('Supprimer', async () => { if (await askConfirm(label)) { fn(); save(); render(); } }, 'btn-sm btn-danger');
const row = (...kids) => h('li', { class: 'row' }, ...kids);
const persist = fn => d => { if (fn(d) === false) return false; if (!save()) return false; render(); return false; };

function matchParrain(name) {
  const t = norm(name); if (t.length < 3) return null;
  const exact = db.members.filter(m => norm(m.nom) === t); if (exact.length === 1) return exact[0];
  const part = db.members.filter(m => norm(m.nom).includes(t)); return part.length === 1 ? part[0] : null;
}
function admDemandes() {
  const list = [...db.demandes].reverse();
  return h('div', { class: 'panel', role: 'tabpanel' }, list.length ? h('ul', { class: 'rows' }, list.map(d => row(
    h('div', {}, h('b', {}, d.nom), h('span', { class: 'tag' }, d.type === 'adhesion' ? 'Adhésion' : 'Braderie'), h('span', { class: 'tag ' + (d.statut === 'nouvelle' ? 'hot' : '') }, d.statut)),
    h('p', { class: 'muted' }, [d.email, d.tel].filter(Boolean).join(' · ')),
    d.parrain ? h('p', { class: 'muted' }, 'Parrain indiqué : ' + d.parrain) : null, d.note ? h('p', { class: 'muted' }, d.note) : null,
    d.statut === 'nouvelle' ? h('div', { class: 'acts' },
      d.type === 'adhesion' ? (() => {
        const guess = matchParrain(d.parrain), sel = h('select', { 'aria-label': 'Parrain de ' + d.nom }, h('option', { value: '' }, 'Aucun parrain'), db.members.map(m => h('option', { value: m.id, selected: guess && m.id === guess.id }, m.nom)));
        return [h('label', { class: 'mini' }, 'Parrain', sel), btn('Accepter', () => acceptDemande(d, sel.value), 'btn-sm')];
      })() : btn('Marquer traitée', () => { d.statut = 'traitée'; save(); render(); }, 'btn-sm'),
      d.type === 'adhesion' ? btn('Refuser', () => { d.statut = 'refusée'; save(); render(); }, 'btn-sm btn-line') : null) : null)))
    : h('p', { class: 'empty' }, 'Aucune demande pour le moment.'));
}
function acceptDemande(d, parrainId) {
  if (emailTaken(d.email)) { toast('Cet e-mail est déjà utilisé par un membre. Refuse la demande ou corrige le membre existant.'); return; }
  const code = String(Math.floor(1000 + Math.random() * 9000));
  db.members.push({ id: uid(), nom: d.nom, role: 'Membre', tel: d.tel || '', email: norm(d.email), numero: nextNumero(), expire: addYear(today()), code, parrainId: parrainId || null });
  d.statut = 'acceptée'; save(); render();
  toast(`Membre créé. Code de connexion à lui transmettre : ${code}`);
}
function changeRole(m, next) {
  if (BUREAU.includes(m.role) && !BUREAU.includes(next)) {
    if (m.id === sessionId) { toast('Tu ne peux pas retirer ton propre rôle : demande à un autre membre du bureau.'); render(); return; }
    if (bureauCount() <= 1) { toast('Il faut au moins un membre du bureau.'); render(); return; }
  }
  m.role = next; save(); render();
}
function removeMember(id) {
  db.members = db.members.filter(x => x.id !== id);
  db.members.forEach(x => { if (x.parrainId === id) x.parrainId = null; });
  Object.keys(db.rsvp).forEach(k => { db.rsvp[k] = db.rsvp[k].filter(x => x !== id); });
  Object.keys(db.pointage).forEach(k => { db.pointage[k] = db.pointage[k].filter(x => x !== id); });
}
function admMembres() {
  return h('div', { class: 'panel', role: 'tabpanel' },
    h('ul', { class: 'rows' }, db.members.map(m => row(
      h('div', {}, h('b', {}, m.nom), h('span', { class: 'tag' }, 'n° ' + m.numero), h('span', { class: 'tag ' + (isValid(m) ? '' : 'hot') }, isValid(m) ? 'à jour' : 'expirée')),
      h('p', { class: 'muted' }, `${m.email} · ${m.tel} · valable jusqu’au ${fmtLong(m.expire)} · code ${m.code}`),
      h('div', { class: 'acts' },
        h('select', { 'aria-label': 'Rôle de ' + m.nom, onchange: e => changeRole(m, e.target.value) }, ROLES.map(r => h('option', { value: r, selected: r === m.role }, r))),
        btn('Renouveler 1 an', () => { m.expire = addYear(m.expire); save(); render(); toast('Cotisation renouvelée.'); }, 'btn-sm'),
        m.id !== sessionId && !(BUREAU.includes(m.role) && bureauCount() <= 1) ? del('Supprimer ' + m.nom + ' ?', () => removeMember(m.id)) : null)))),
    h('h2', {}, 'Ajouter un membre'),
    form([field('Prénom et nom', input('nom', { req: true })), field('E-mail', input('email', { type: 'email', req: true })), field('Téléphone', input('tel', { type: 'tel' })), field('Code personnel (4 chiffres)', input('code', { req: true, mode: 'numeric', ph: '1234', pattern: '[0-9]{4}', title: '4 chiffres' }))],
      persist(d => {
        if (!/^\d{4}$/.test(d.code.trim())) { toast('Le code doit comporter exactement 4 chiffres.'); return false; }
        if (emailTaken(d.email)) { toast('Cet e-mail est déjà utilisé par un membre.'); return false; }
        db.members.push({ id: uid(), nom: d.nom, role: 'Membre', tel: d.tel, email: norm(d.email), numero: nextNumero(), expire: addYear(today()), code: d.code.trim(), parrainId: null });
      }), 'Ajouter'));
}
function admPointage() {
  const evs = [...db.events].sort((a, b) => b.date.localeCompare(a.date));
  return h('div', { class: 'panel', role: 'tabpanel' }, h('p', { class: 'muted' }, 'Coche les membres réellement présents à chaque événement.'),
    evs.length ? evs.map(ev => {
      const here = new Set(db.pointage[ev.id] || []), ann = new Set(db.rsvp[ev.id] || []);
      return h('fieldset', { class: 'point' }, h('legend', {}, ev.titre + ' · ' + fmtLong(ev.date)),
        h('p', { class: 'note' }, `${here.size} présent(s) sur ${db.members.length} · ${ann.size} annoncé(s)`),
        db.members.map(m => h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: here.has(m.id), onchange: e => { const s = new Set(db.pointage[ev.id] || []); e.target.checked ? s.add(m.id) : s.delete(m.id); db.pointage[ev.id] = [...s]; save(); render(); } }), m.nom, ann.has(m.id) ? h('span', { class: 'tag' }, 'annoncé') : null)));
    }) : h('p', { class: 'empty' }, 'Aucun événement.'));
}
function admSorties() {
  return h('div', { class: 'panel', role: 'tabpanel' },
    h('ul', { class: 'rows' }, [...db.events].sort((a, b) => a.date.localeCompare(b.date)).map(ev => row(h('div', {}, h('b', {}, ev.titre), h('span', { class: 'tag' }, fmtLong(ev.date))), del('Supprimer « ' + ev.titre + ' » ?', () => { db.events = db.events.filter(x => x.id !== ev.id); })))),
    h('h2', {}, 'Ajouter une sortie'),
    form([field('Titre', input('titre', { req: true })), field('Date', input('date', { type: 'date', req: true })), field('Description', textarea('desc', { rows: 3 })), field('Lien de l’itinéraire 68°', input('lien', { type: 'url', ph: 'https://…' }), '(facultatif)'), h('label', { class: 'check' }, h('input', { type: 'checkbox', name: 'inscription' }), 'Inscription obligatoire')],
      persist(d => db.events.push({ id: uid(), titre: d.titre, date: d.date, desc: d.desc || '', lien: /^https:\/\//.test(d.lien || '') ? d.lien : '', inscription: !!d.inscription })), 'Ajouter'));
}
function admInfos() {
  return h('div', { class: 'panel', role: 'tabpanel' },
    h('ul', { class: 'rows' }, [...db.communiques].sort((a, b) => b.date.localeCompare(a.date)).map(c => row(h('div', {}, h('b', {}, c.titre), h('span', { class: 'tag' }, fmtLong(c.date))), del('Supprimer ce communiqué ?', () => { db.communiques = db.communiques.filter(x => x.id !== c.id); })))),
    h('h2', {}, 'Publier un communiqué'),
    form([field('Titre', input('titre', { req: true })), field('Message', textarea('texte', { req: true, rows: 5 }))],
      persist(d => db.communiques.push({ id: uid(), date: today(), titre: d.titre, texte: d.texte })), 'Publier'));
}
function admStock() {
  const f = currentFiche();
  const newFiche = form([field('Nom de l’événement', input('titre', { req: true, ph: 'Journée huîtres' })), field('Date', input('date', { type: 'date', req: true }))],
    persist(d => { const nf = { id: uid(), titre: d.titre.trim(), date: d.date, items: [] }; db.fiches.push(nf); ficheId = nf.id; toast('Fiche créée : ajoute ce que vous achetez.'); }), 'Créer la fiche');
  if (!f) return h('div', { class: 'panel', role: 'tabpanel' }, h('p', { class: 'empty' }, 'Aucune fiche. Crée la première.'), h('h2', {}, 'Nouvelle fiche'), newFiche);
  const numField = (i, key, label, min) => h('label', { class: 'mini' }, label, h('input', { type: 'number', min, step: key === 'prix' ? '0.01' : 'any', value: i[key], onchange: e => {
    const v = e.target.value === '' && key === 'prix' ? '' : Number(e.target.value);
    if (v !== '' && (!Number.isFinite(v) || v < 0)) { toast(label + ' invalide : un nombre positif ou zéro.'); e.target.value = i[key]; return; }
    i[key] = v; save(); } }));
  return h('div', { class: 'panel', role: 'tabpanel' }, fichePicker(),
    h('ul', { class: 'rows' }, f.items.map(i => row(
      h('div', {}, h('b', {}, i.nom), h('span', { class: 'tag' }, i.categorie)),
      h('div', { class: 'acts' }, numField(i, 'qte', 'Quantité', '0'),
        h('label', { class: 'mini' }, 'Unité', h('input', { type: 'text', value: i.unite, onchange: e => { i.unite = e.target.value.trim(); save(); } })),
        numField(i, 'prix', 'Prix payé (€)', '0'),
        del('Retirer ' + i.nom + ' de la fiche ?', () => { f.items = f.items.filter(x => x.id !== i.id); }))))),
    h('h2', {}, 'Ajouter à la fiche'),
    form([field('Article', input('nom', { req: true, ph: 'Vin blanc' })),
      field('Catégorie', h('select', { name: 'categorie' }, CATS.map(c => h('option', { value: c }, c)))),
      field('Quantité', input('qte', { type: 'number', req: true, min: '0', value: '1' })), field('Unité', input('unite', { ph: 'bouteilles' }), '(facultatif)'),
      field('Prix payé en €', input('prix', { type: 'number', min: '0' }), '(facultatif)')],
      persist(d => {
        const qte = Number(d.qte), prix = d.prix === '' ? '' : Number(d.prix);
        if (!Number.isFinite(qte) || qte < 0 || (prix !== '' && (!Number.isFinite(prix) || prix < 0))) { toast('Quantité et prix doivent être positifs.'); return false; }
        f.items.push({ id: uid(), nom: d.nom.trim(), categorie: CATS.includes(d.categorie) ? d.categorie : 'Autre', qte, unite: (d.unite || '').trim(), prix });
      }), 'Ajouter'),
    h('h2', {}, 'Préparer la prochaine édition'),
    h('p', { class: 'note' }, 'Copie cette fiche avec les mêmes articles et quantités, à ajuster.'),
    form([field('Nom de la nouvelle fiche', input('titre', { req: true, value: f.titre })), field('Date', input('date', { type: 'date', req: true }))],
      persist(d => { const nf = { id: uid(), titre: d.titre.trim(), date: d.date, items: f.items.map(i => ({ ...i, id: uid(), prix: '' })) }; db.fiches.push(nf); ficheId = nf.id; toast('Fiche copiée : ajuste les quantités.'); }), 'Copier la fiche'),
    h('h2', {}, 'Nouvelle fiche vide'), newFiche,
    del('Supprimer toute la fiche « ' + f.titre + ' » ?', () => { db.fiches = db.fiches.filter(x => x.id !== f.id); ficheId = null; }));
}
function admPartenaires() {
  return h('div', { class: 'panel', role: 'tabpanel' },
    h('ul', { class: 'rows' }, db.partenaires.map(p => row(h('div', {}, h('b', {}, p.nom), h('span', { class: 'tag' }, p.remise)), del('Supprimer ' + p.nom + ' ?', () => { db.partenaires = db.partenaires.filter(x => x.id !== p.id); })))),
    h('h2', {}, 'Ajouter un partenaire'),
    form([field('Commerce', input('nom', { req: true })), field('Remise offerte aux adhérents', input('remise', { req: true, ph: '−10 % sur les pièces' }))],
      persist(d => db.partenaires.push({ id: uid(), nom: d.nom, remise: d.remise })), 'Ajouter'));
}
function admAlbums() {
  return h('div', { class: 'panel', role: 'tabpanel' }, h('p', { class: 'muted' }, 'Pour ajouter des photos, ouvre l’album depuis la galerie.'),
    h('ul', { class: 'rows' }, db.albums.map(a => row(h('div', {}, h('b', {}, a.titre), h('span', { class: 'tag' }, a.photos.length + ' photo(s)')), h('div', { class: 'acts' }, h('a', { class: 'btn btn-sm', href: '#album-' + a.id }, 'Ouvrir'), del('Supprimer l’album « ' + a.titre + ' » et ses photos ?', () => { dropPhotos(a); db.albums = db.albums.filter(x => x.id !== a.id); }))))),
    h('h2', {}, 'Créer un album'),
    form([field('Titre', input('titre', { req: true, ph: 'Sortie de printemps 2027' })), field('Année', input('annee', { type: 'number', req: true, value: String(new Date().getFullYear()) }))],
      persist(d => db.albums.unshift({ id: uid(), titre: d.titre, annee: Number(d.annee), couleur: (db.albums.length % 3) + 1, photos: [] })), 'Créer'));
}
function admReglages() {
  const s = db.settings;
  return h('div', { class: 'panel', role: 'tabpanel' },
    form([field('Lien de la chaîne YouTube', input('youtube', { type: 'url', value: s.youtube, ph: 'https://www.youtube.com/@…' })), field('Date de la braderie', input('braderieDate', { value: s.braderieDate })), field('Lieu de la braderie', input('braderieLieu', { value: s.braderieLieu }))],
      d => { if (d.youtube && !/^https:\/\//.test(d.youtube)) { toast('Le lien doit commencer par https://'); return false; } Object.assign(s, d); save(); render(); toast('Réglages enregistrés.'); return false; }),
    h('h2', {}, 'Données de démonstration'),
    h('p', { class: 'note' }, 'Remet tout à zéro : membres, sorties, photos et demandes d’exemple.'),
    btn('Réinitialiser la démo', async () => { if (await askConfirm('Tout effacer et recharger les données d’exemple ?', 'Tout effacer')) { db = seed(); setSession(null); photoStore.clear(); save(); location.hash = '#accueil'; render(); } }, 'btn-danger btn-full'),
    persistent ? null : h('p', { class: 'note' }, 'Stockage indisponible : les changements disparaissent à la fermeture.'));
}

/* ---------- Routeur ---------- */
const sheet = $('#sheet'), burger = $('#burger'), view = $('#view');
view.tabIndex = -1;
const TABS = { accueil: 'accueil', sorties: 'sorties', galerie: 'galerie', membres: 'membres', connexion: 'membres', admin: 'membres' };
function route() {
  const name = (location.hash || '#accueil').slice(1);
  if (name.startsWith('album-')) return { tab: 'galerie', title: 'Album', node: () => viewAlbum(name.slice(6)) };
  const map = { accueil: viewAccueil, sorties: viewSorties, galerie: viewGalerie, membres: viewMembres, connexion: viewConnexion, admin: viewAdmin, partenaires: viewPartenaires, braderie: viewBraderie, rejoindre: viewRejoindre, securite: viewSecurite };
  const fn = map[name] || viewAccueil;
  return { tab: TABS[name] || (map[name] ? '' : 'accueil'), title: name, node: fn };
}
let lastRoute = null, popId = null;
function stagger() {
  const root = view.querySelector('.pad');
  if (root) [...root.children].slice(0, 10).forEach((c, i) => c.style.setProperty('--i', i));
  view.querySelectorAll('.hero > *').forEach((c, i) => c.style.setProperty('--i', i));
}
function render() {
  const r = route(), key = location.hash || '#accueil', changed = key !== lastRoute;
  lastRoute = key;
  view.classList.remove('enter');                      // l'entrée ne rejoue pas lors d'un simple re-rendu
  if (changed) query = '';
  view.replaceChildren(...[r.node()].flat());
  popId = null;
  if (changed) {
    stagger(); void view.offsetWidth; view.classList.add('enter');
    view.focus({ preventScroll: true });
    const t = $('h1', view); document.title = (t ? t.textContent + ' · ' : '') + 'Béruges Moto Club';
  }
  document.querySelectorAll('.tabbar a').forEach(a => a.dataset.tab === r.tab ? a.setAttribute('aria-current', 'page') : a.removeAttribute('aria-current'));
  buildSheet();
}
function buildSheet() {
  const m = me();
  const links = [['#partenaires', 'Partenaires et remises'], ['#braderie', 'Braderie'], ['#rejoindre', 'Rejoindre le club'], ['#securite', 'Rouler en sécurité']];
  sheet.replaceChildren(
    ...links.map(([href, l]) => h('a', { href }, l)),
    db.settings.youtube ? h('a', { href: db.settings.youtube, target: '_blank', rel: 'noopener' }, 'Chaîne YouTube') : null,
    isAdmin() ? h('a', { href: '#admin' }, 'Administration du bureau') : null,
    m ? h('button', { type: 'button', onclick: () => { setSession(null); location.hash = '#accueil'; closeSheet(); render(); toast('Déconnecté.'); } }, 'Se déconnecter') : h('a', { href: '#connexion' }, 'Se connecter'));
}
function closeSheet() { sheet.hidden = true; burger.setAttribute('aria-expanded', 'false'); }
burger.addEventListener('click', () => { const open = sheet.hidden; sheet.hidden = !open; burger.setAttribute('aria-expanded', String(open)); });
document.addEventListener('keydown', e => { if (e.key === 'Escape') closeSheet(); });
sheet.addEventListener('click', e => { if (e.target.closest('a,button')) closeSheet(); });
window.addEventListener('hashchange', () => { closeSheet(); render(); window.scrollTo(0, 0); });

load(); render();

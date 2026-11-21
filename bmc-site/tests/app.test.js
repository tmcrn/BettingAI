'use strict';
/*
 * Tests de bout en bout de l'appli BMC.
 *   npm install && npx playwright install chromium
 *   npm test            tout
 *   npm run test:marche seulement ce qui DOIT marcher
 *   npm run test:refuse seulement ce qui DOIT être refusé
 * Chaque test ouvre un navigateur neuf (localStorage vide) : aucun test ne dépend d'un autre.
 */
const { test, describe, before, after } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

let chromium;
try { ({ chromium } = require('playwright')); } catch { console.error('Playwright est absent : lance `npm install` dans bmc-site.'); process.exit(1); }

const SITE = path.join(__dirname, '..');
const URL_ = pathToFileURL(path.join(SITE, 'index.html')).href;
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==', 'base64');
let browser;
before(async () => { browser = await chromium.launch(); });
after(async () => { await browser.close(); });

/* ---------- Outils ---------- */
async function open(hash = '', { init } = {}) {
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, timezoneId: 'Europe/Paris', locale: 'fr-FR' });
  if (init) await ctx.addInitScript(init);
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error' && !/fonts\.g|ERR_|net::/i.test(m.text())) errors.push(m.text()); });
  await page.goto(URL_ + hash);
  return { page, ctx, errors };
}
/** Les sorties de démonstration sont datées de 2027 : on les repousse pour que les tests ne vieillissent pas. */
const futureEvents = page => page.evaluate(() => { db.events.forEach((e, i) => { e.date = `2099-0${i + 1}-10`; }); save(); render(); });
async function login(page, email, code) {
  await page.goto(URL_ + '#connexion');
  await page.getByLabel('E-mail').fill(email);
  await page.getByLabel('Code personnel').fill(code);
  await page.getByRole('button', { name: 'Se connecter' }).click();
  await page.getByRole('tab', { name: 'Annuaire' }).waitFor();
}
const asPresident = page => login(page, 'president@bmc.test', '1111');
const asMember = page => login(page, 'membre@bmc.test', '4444');
async function admin(page, tab) { await page.goto(URL_ + '#admin'); await page.getByRole('tab', { name: tab }).click(); }
const toastText = page => page.locator('#toast').textContent();
const ev = (page, fn, arg) => page.evaluate(fn, arg);
const formWith = (page, label) => page.locator('form', { has: page.getByLabel(label) });
/** Attend qu'une condition évaluée dans la page devienne vraie (le rendu est asynchrone). */
const until = (page, fn, arg) => page.waitForFunction(fn, arg, { timeout: 5000 });
const rowCount = page => page.locator('.rows .row').count();

/* =====================================================================
 *  CE QUI DOIT MARCHER
 * ===================================================================== */
describe('MARCHE', () => {
  test('l’accueil se charge sans erreur et affiche le club', async () => {
    const { page, ctx, errors } = await open();
    assert.match(await page.title(), /Béruges Moto Club/);
    assert.equal(await page.locator('h1').first().textContent(), 'Béruges Moto Club');
    assert.ok(await page.getByText('Contacts du club').isVisible());
    assert.deepEqual(errors, []);
    await ctx.close();
  });

  test('les onglets affichent le bon écran et marquent l’onglet actif', async () => {
    const { page, ctx } = await open();
    await page.getByRole('link', { name: 'Sorties', exact: true }).click();
    await page.locator('h1', { hasText: 'Prochaines sorties' }).waitFor();
    assert.equal(await page.getByRole('link', { name: 'Sorties', exact: true }).getAttribute('aria-current'), 'page');
    await page.getByRole('link', { name: 'Galerie', exact: true }).click();
    await page.locator('h1', { hasText: 'Galerie' }).waitFor();
    assert.equal(await page.getByRole('link', { name: 'Sorties', exact: true }).getAttribute('aria-current'), null);
    await ctx.close();
  });

  test('l’accueil montre la prochaine sortie à venir', async () => {
    const { page, ctx } = await open();
    await futureEvents(page);
    assert.ok(await page.locator('.event', { hasText: 'Sortie de printemps' }).isVisible());
    await ctx.close();
  });

  test('le formulaire d’adhésion complet crée une demande pour le bureau', async () => {
    const { page, ctx } = await open('#rejoindre');
    await page.getByLabel('Prénom et nom').fill('Jean Test');
    await page.getByLabel('E-mail').fill('jean@test.fr');
    await page.getByLabel('Téléphone').fill('0611223344');
    await page.getByLabel('Parrainé par').fill('Prénom Président');
    await page.getByRole('button', { name: 'Envoyer ma demande' }).click();
    await until(page, () => db.demandes.length === 1);
    assert.deepEqual(await ev(page, () => [db.demandes[0].type, db.demandes[0].statut, db.demandes[0].parrain]), ['adhesion', 'nouvelle', 'Prénom Président']);
    await ctx.close();
  });

  test('connexion réussie puis déconnexion', async () => {
    const { page, ctx } = await open();
    await asMember(page);
    assert.ok(await page.getByText('Connecté :').isVisible());
    await page.getByRole('button', { name: 'Se déconnecter' }).click();
    await until(page, () => me() === null);
    assert.equal(await page.locator('h1').first().textContent(), 'Béruges Moto Club');
    await ctx.close();
  });

  test('« Je viens » compte, change le bouton et persiste après rechargement', async () => {
    const { page, ctx } = await open();
    await futureEvents(page);
    await login(page, 'autre@bmc.test', '5555');
    await page.goto(URL_ + '#sorties');
    const first = page.locator('.event').first();
    assert.match(await first.textContent(), /2 membres viennent/);
    await first.getByRole('button', { name: 'Je viens' }).click();
    await until(page, () => /3 membres viennent/.test(document.querySelector('.event').textContent));
    assert.equal(await page.locator('.event').first().getByRole('button').getAttribute('aria-pressed'), 'true');
    await page.reload();
    assert.equal(await page.locator('.event').first().getByRole('button').getAttribute('aria-pressed'), 'true');
    await ctx.close();
  });

  test('la recherche filtre l’annuaire sans rejouer l’animation d’entrée', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await page.waitForTimeout(1200); // laisse finir l’entrée de l’écran
    await page.getByLabel('Rechercher un membre').fill('Secr');
    await until(page, () => document.querySelectorAll('.people li').length === 1);
    assert.equal(await ev(page, () => document.getAnimations().filter(a => a.animationName === 'monte').length), 0);
    assert.ok(await page.getByLabel('Rechercher un membre').evaluate(el => el === document.activeElement), 'le champ garde le focus');
    await ctx.close();
  });

  test('la carte affiche le parrain, les filleuls et la cotisation', async () => {
    const { page, ctx } = await open();
    await asMember(page);
    await page.getByRole('tab', { name: 'Ma carte' }).click();
    await page.getByText('Cotisation à jour').waitFor();
    const text = await page.locator('#mem-panel').textContent();
    assert.match(text, /Parrain : Prénom Président/);
    assert.match(text, /Tu as parrainé 1 membre : Autre Membre/);
    await ctx.close();
  });

  test('les onglets sont reliés à leur panneau et se parcourent aux flèches', async () => {
    const { page, ctx } = await open();
    await asMember(page);
    const tab = page.getByRole('tab', { name: 'Annuaire' });
    assert.equal(await tab.getAttribute('aria-controls'), 'mem-panel');
    assert.equal(await page.locator('#mem-panel').getAttribute('aria-labelledby'), 'mem-tab-annuaire');
    await tab.focus();
    await page.keyboard.press('ArrowRight');
    await until(page, () => document.activeElement && document.activeElement.id === 'mem-tab-carte');
    assert.equal(await page.getByRole('tab', { name: 'Ma carte' }).getAttribute('aria-selected'), 'true');
    await ctx.close();
  });

  test('le bureau change le rôle d’un autre membre', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Membres');
    await page.getByLabel('Rôle de Prénom Trésorier').selectOption('Membre');
    await until(page, () => db.members.find(m => m.id === 'm3').role === 'Membre');
    await ctx.close();
  });

  test('confirmer une suppression retire l’élément', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Partenaires');
    assert.equal(await rowCount(page), 3);
    await page.locator('.rows .row').first().getByRole('button', { name: 'Supprimer' }).click();
    await page.locator('dialog.confirm').getByRole('button', { name: 'Supprimer' }).click();
    await until(page, () => document.querySelectorAll('.rows .row').length === 2);
    await ctx.close();
  });

  test('supprimer un membre nettoie parrain, présences et pointage', async () => {
    const { page, ctx } = await open();
    const out = await ev(page, () => {
      db.pointage.e1 = ['m4', 'm1'];
      removeMember('m4');
      return { membre: db.members.some(m => m.id === 'm4'), filleul: db.members.find(m => m.id === 'm5').parrainId, rsvp: db.rsvp.e1, pointage: db.pointage.e1 };
    });
    assert.deepEqual(out, { membre: false, filleul: null, rsvp: ['m1'], pointage: ['m1'] });
    await ctx.close();
  });

  test('accepter une demande crée le membre avec le parrain choisi', async () => {
    const { page, ctx } = await open();
    await ev(page, () => db.demandes.push({ id: 'd1', type: 'adhesion', nom: 'Léa Test', email: 'Lea@Test.fr', tel: '0611223355', parrain: 'autre membre', date: today(), statut: 'nouvelle' }));
    await asPresident(page);
    await admin(page, 'Demandes');
    assert.equal(await page.getByLabel('Parrain de Léa Test').inputValue(), 'm5', 'parrain unique suggéré');
    await page.getByRole('button', { name: 'Accepter' }).click();
    await until(page, () => db.members.some(m => m.email === 'lea@test.fr' && m.parrainId === 'm5'));
    assert.match(await toastText(page), /Membre créé/);
    await ctx.close();
  });

  test('ajouter un membre avec un code valide et un e-mail neuf', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Membres');
    const before = await ev(page, () => db.members.length);
    await page.getByLabel('Prénom et nom').fill('Nouveau Membre');
    await page.getByLabel('E-mail').fill('nouveau@test.fr');
    await page.getByLabel(/Code personnel/).fill('1234');
    await page.getByRole('button', { name: 'Ajouter' }).click();
    await until(page, n => db.members.length === n + 1, before);
    await ctx.close();
  });

  test('un communiqué publié par le bureau est lu par les membres', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Communiqués');
    await page.getByLabel('Titre').fill('Sortie annulée');
    await page.getByLabel('Message').fill('Météo trop mauvaise.');
    await page.getByRole('button', { name: 'Publier' }).click();
    await until(page, () => db.communiques.some(c => c.titre === 'Sortie annulée'));
    await ev(page, () => { setSession(null); });
    await asMember(page);
    await page.getByRole('tab', { name: 'Communiqués' }).click();
    assert.ok(await page.getByRole('heading', { name: 'Sortie annulée' }).isVisible());
    await ctx.close();
  });

  test('le pointage est enregistré et survit au rechargement', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Pointage');
    await page.locator('fieldset.point').first().getByRole('checkbox').first().click();
    await until(page, () => Object.values(db.pointage).some(l => l.length === 1));
    await page.reload();
    await page.getByRole('tab', { name: 'Pointage' }).click();
    assert.equal(await page.locator('fieldset.point').first().getByRole('checkbox').first().isChecked(), true);
    await ctx.close();
  });

  test('inventaire : les membres lisent la fiche d’événement par catégories', async () => {
    const { page, ctx } = await open();
    await asMember(page);
    await page.getByRole('tab', { name: 'Inventaire' }).click();
    await page.getByLabel('Choisir l’événement').waitFor();
    const text = await page.locator('#mem-panel').textContent();
    for (const mot of ['Journée huîtres', 'Nourriture', 'Boissons', 'Matériel', 'Huîtres', 'Vin blanc', 'Bières', 'Charcuterie', 'Tivolis', 'Tables', 'Vaisselle']) assert.match(text, new RegExp(mot), mot);
    await ctx.close();
  });

  test('inventaire : le bureau ajoute un achat avec son prix et le total apparaît', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Inventaire');
    const f = formWith(page, 'Article');
    await f.getByLabel('Article').fill('Glaçons');
    await f.getByLabel('Catégorie').selectOption('Boissons');
    await f.getByLabel('Quantité').fill('20');
    await f.getByLabel('Unité').fill('sacs');
    await f.getByLabel(/Prix payé/).fill('45');
    await f.getByRole('button', { name: 'Ajouter' }).click();
    await until(page, () => currentFiche().items.some(i => i.nom === 'Glaçons' && i.qte === 20 && i.prix === 45));
    await ev(page, () => { setSession('m4'); location.hash = '#membres'; });
    await page.getByRole('tab', { name: 'Inventaire' }).click();
    assert.match(await page.locator('#mem-panel').textContent(), /Glaçons20 sacs/);
    assert.match(await page.locator('#mem-panel').textContent(), /Total acheté : 45,00\s€/);
    await ctx.close();
  });

  test('inventaire : modifier une quantité est enregistré', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Inventaire');
    const qty = page.locator('.rows .row').first().getByLabel('Quantité');
    await qty.fill('99'); await qty.blur();
    await until(page, () => currentFiche().items[0].qte === 99);
    await page.reload();
    assert.equal(await ev(page, () => currentFiche().items[0].qte), 99);
    await ctx.close();
  });

  test('inventaire : copier la fiche prépare la prochaine édition', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Inventaire');
    const nb = await ev(page, () => currentFiche().items.length);
    const f = formWith(page, 'Nom de la nouvelle fiche');
    await f.getByLabel('Nom de la nouvelle fiche').fill('Journée huîtres 2027');
    await f.getByLabel('Date').fill('2027-10-17');
    await f.getByRole('button', { name: 'Copier la fiche' }).click();
    await until(page, () => db.fiches.length === 2);
    const copie = await ev(page, () => ({ titre: currentFiche().titre, items: currentFiche().items.length, prixVides: currentFiche().items.every(i => i.prix === ''), idsNeufs: currentFiche().items.every(i => !db.fiches[0].items.some(o => o.id === i.id)) }));
    assert.deepEqual(copie, { titre: 'Journée huîtres 2027', items: nb, prixVides: true, idsNeufs: true });
    await ctx.close();
  });

  test('une photo ajoutée par le bureau apparaît et reste après rechargement', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await page.goto(URL_ + '#album-a1');
    await page.locator('input[type=file]').setInputFiles({ name: 'p.png', mimeType: 'image/png', buffer: PNG });
    await page.locator('.photo img[src^="data:"]').first().waitFor();
    assert.equal(await page.locator('.photo').count(), 1);
    await page.reload();
    await page.locator('.photo img[src^="data:"]').first().waitFor({ timeout: 5000 });
    assert.equal(await page.locator('.photo.missing').count(), 0);
    assert.equal(await ev(page, () => db.albums[0].photos.every(p => !p.startsWith('data:'))), true, 'la base ne contient que des identifiants');
    await ctx.close();
  });

  test('réglages : un lien YouTube https est enregistré et affiché sur l’accueil', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Réglages');
    await page.getByLabel('Lien de la chaîne YouTube').fill('https://www.youtube.com/@bmc');
    await page.getByRole('button', { name: 'Enregistrer' }).click();
    await until(page, () => db.settings.youtube === 'https://www.youtube.com/@bmc');
    await page.goto(URL_ + '#accueil');
    assert.ok(await page.getByRole('link', { name: 'Voir nos vidéos sur YouTube' }).isVisible());
    await ctx.close();
  });

  test('une sortie avec un lien 68° https affiche « Itinéraire sur 68° »', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Sorties');
    const f = formWith(page, 'Titre');
    await f.getByLabel('Titre').fill('Balade test');
    await f.getByLabel('Date').fill('2099-12-01');
    await f.getByLabel(/Lien de l’itinéraire/).fill('https://app68.eu/roadtrip/test');
    await f.getByRole('button', { name: 'Ajouter' }).click();
    await until(page, () => db.events.some(e => e.titre === 'Balade test' && e.lien === 'https://app68.eu/roadtrip/test'));
    await page.goto(URL_ + '#sorties');
    assert.ok(await page.getByRole('link', { name: 'Itinéraire sur 68°' }).isVisible());
    await ctx.close();
  });

  test('la date du jour suit le fuseau local, pas l’UTC', async () => {
    const { page, ctx } = await open();
    assert.equal(await ev(page, () => isoLocal(new Date('2027-03-14T23:30:00Z'))), '2027-03-15'); // 00 h 30 à Paris
    await ctx.close();
  });

  test('addYear gère le 29 février et les cotisations expirées', async () => {
    const { page, ctx } = await open();
    assert.equal(await ev(page, () => addYear('2099-02-29')), '2100-02-28');
    assert.equal(await ev(page, () => addYear('2099-12-31')), '2100-12-31');
    assert.equal(await ev(page, () => addYear('2001-06-30')), `${new Date().getFullYear() + 1}-06-30`);
    await ctx.close();
  });

  test('des données anciennes sans les clés récentes ne font pas planter l’appli', async () => {
    const { page, ctx, errors } = await open('', { init: () => localStorage.setItem('bmc-data-v1', JSON.stringify({ members: [{ id: 'x', nom: 'Ancien', role: 'Membre', email: 'a@a.fr', code: '1', numero: '0009', expire: '2030-01-01', tel: '' }], settings: { youtube: '' } })) });
    assert.equal(await ev(page, () => Array.isArray(db.demandes) && Array.isArray(db.events) && Array.isArray(db.partenaires) && Array.isArray(db.fiches)), true);
    assert.ok(await ev(page, () => db.settings.contacts.length > 0));
    assert.deepEqual(errors, []);
    await ctx.close();
  });
});

/* =====================================================================
 *  CE QUI DOIT ÊTRE REFUSÉ (et ne rien casser)
 * ===================================================================== */
describe('REFUSÉ', () => {
  test('un mauvais code est refusé et on reste sur la connexion', async () => {
    const { page, ctx } = await open('#connexion');
    await page.getByLabel('E-mail').fill('membre@bmc.test');
    await page.getByLabel('Code personnel').fill('0000');
    await page.getByRole('button', { name: 'Se connecter' }).click();
    await until(page, () => /incorrect/.test(document.getElementById('toast').textContent));
    assert.ok(await page.getByLabel('Code personnel').isVisible());
    assert.equal(await ev(page, () => me()), null);
    await ctx.close();
  });

  test('une connexion avec les champs vides n’envoie rien', async () => {
    const { page, ctx } = await open('#connexion');
    await page.getByRole('button', { name: 'Se connecter' }).click();
    await page.waitForTimeout(300);
    assert.equal(await ev(page, () => me()), null);
    assert.equal(await page.locator('form.form').count(), 1);
    await ctx.close();
  });

  test('le formulaire d’adhésion vide ne crée aucune demande', async () => {
    const { page, ctx } = await open('#rejoindre');
    await page.getByRole('button', { name: 'Envoyer ma demande' }).click();
    await page.waitForTimeout(300);
    assert.equal(await ev(page, () => db.demandes.length), 0);
    await ctx.close();
  });

  test('sans connexion, on ne peut pas répondre à une sortie', async () => {
    const { page, ctx } = await open('#sorties');
    await futureEvents(page);
    assert.equal(await page.getByRole('button', { name: 'Je viens' }).count(), 0);
    assert.ok(await page.getByRole('link', { name: 'Se connecter pour répondre' }).first().isVisible());
    await ctx.close();
  });

  test('sans connexion, l’administration renvoie vers la connexion', async () => {
    const { page, ctx } = await open('#admin');
    assert.ok(await page.getByLabel('Code personnel').isVisible());
    await ctx.close();
  });

  test('un simple membre n’accède pas à l’administration', async () => {
    const { page, ctx } = await open();
    await asMember(page);
    assert.equal(await page.getByText('Accès administrateur du bureau').count(), 0);
    await page.goto(URL_ + '#admin');
    await page.getByText('Accès réservé').first().waitFor();
    await ctx.close();
  });

  test('un membre du bureau ne peut pas se retirer son propre rôle', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Membres');
    await page.getByLabel('Rôle de Prénom Président').selectOption('Membre');
    await until(page, () => /propre rôle/.test(document.getElementById('toast').textContent));
    assert.equal(await ev(page, () => me().role), 'Président');
    assert.ok(await page.getByRole('heading', { name: 'Administration' }).isVisible());
    await ctx.close();
  });

  test('on ne peut pas retirer le dernier membre du bureau', async () => {
    const { page, ctx } = await open();
    const role = await ev(page, () => {
      sessionId = 'm4'; // un simple membre agit : la règle du dernier admin doit tenir quand même
      db.members[1].role = 'Membre'; db.members[2].role = 'Membre';
      changeRole(db.members[0], 'Membre');
      return db.members[0].role;
    });
    assert.equal(role, 'Président');
    await ctx.close();
  });

  test('annuler une suppression conserve l’élément', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Partenaires');
    await page.locator('.rows .row').first().getByRole('button', { name: 'Supprimer' }).click();
    await page.locator('dialog.confirm').getByRole('button', { name: 'Annuler' }).click();
    await page.locator('dialog.confirm').waitFor({ state: 'detached' });
    assert.equal(await rowCount(page), 3);
    await ctx.close();
  });

  test('un parrain ambigu ou vide n’est jamais deviné', async () => {
    const { page, ctx } = await open();
    await ev(page, () => {
      db.demandes.push({ id: 'd1', type: 'adhesion', nom: 'Jean Test', email: 'jean@test.fr', tel: '1', parrain: 'Prénom', date: today(), statut: 'nouvelle' });
      db.demandes.push({ id: 'd3', type: 'adhesion', nom: 'Max Test', email: 'max@test.fr', tel: '2', parrain: ' ', date: today(), statut: 'nouvelle' });
    });
    await asPresident(page);
    await admin(page, 'Demandes');
    assert.equal(await page.getByLabel('Parrain de Jean Test').inputValue(), '');
    assert.equal(await page.getByLabel('Parrain de Max Test').inputValue(), '');
    await ctx.close();
  });

  test('une demande avec un e-mail déjà pris est refusée à l’acceptation', async () => {
    const { page, ctx } = await open();
    await ev(page, () => db.demandes.push({ id: 'd2', type: 'adhesion', nom: 'Doublon', email: 'MEMBRE@bmc.test', tel: '1', parrain: '', date: today(), statut: 'nouvelle' }));
    await asPresident(page);
    await admin(page, 'Demandes');
    await page.getByRole('button', { name: 'Accepter' }).click();
    await until(page, () => /déjà utilisé/.test(document.getElementById('toast').textContent));
    assert.equal(await ev(page, () => db.members.length), 5);
    await ctx.close();
  });

  test('ajout de membre : code trop court et e-mail en double refusés', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Membres');
    const fill = async (nom, email, code) => {
      await page.getByLabel('Prénom et nom').fill(nom); await page.getByLabel('E-mail').fill(email); await page.getByLabel(/Code personnel/).fill(code);
      await page.getByRole('button', { name: 'Ajouter' }).click();
    };
    await fill('Code Court', 'court@test.fr', '12');
    await page.waitForTimeout(300);
    assert.equal(await ev(page, () => db.members.length), 5, 'code trop court');
    await fill('Doublon', 'membre@bmc.test', '1234');
    await until(page, () => /déjà utilisé/.test(document.getElementById('toast').textContent));
    assert.equal(await ev(page, () => db.members.length), 5, 'e-mail en double');
    await ctx.close();
  });

  test('inventaire : une quantité négative est refusée et l’ancienne valeur revient', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Inventaire');
    const qty = page.locator('.rows .row').first().getByLabel('Quantité');
    const avant = await ev(page, () => currentFiche().items[0].qte);
    await qty.fill('-3'); await qty.blur();
    await until(page, () => /invalide/.test(document.getElementById('toast').textContent));
    assert.equal(await ev(page, () => currentFiche().items[0].qte), avant);
    assert.equal(await qty.inputValue(), String(avant));
    await ctx.close();
  });

  test('inventaire : une fiche sans nom n’est pas créée', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Inventaire');
    const f = formWith(page, 'Nom de l’événement');
    await f.getByRole('button', { name: 'Créer la fiche' }).click();
    await page.waitForTimeout(300);
    assert.equal(await ev(page, () => db.fiches.length), 1);
    await ctx.close();
  });

  test('inventaire : un article à quantité négative n’est pas ajouté', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Inventaire');
    const nb = await ev(page, () => currentFiche().items.length);
    const f = formWith(page, 'Article');
    await f.getByLabel('Article').fill('Erreur');
    await f.getByLabel('Quantité').fill('-5');
    await f.getByRole('button', { name: 'Ajouter' }).click();
    await page.waitForTimeout(300);
    assert.equal(await ev(page, () => currentFiche().items.length), nb);
    await ctx.close();
  });

  test('réglages : un lien YouTube sans https est refusé', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await admin(page, 'Réglages');
    await page.getByLabel('Lien de la chaîne YouTube').evaluate(el => { el.type = 'text'; });
    await page.getByLabel('Lien de la chaîne YouTube').fill('javascript:alert(1)');
    await page.getByRole('button', { name: 'Enregistrer' }).click();
    await until(page, () => /https/.test(document.getElementById('toast').textContent));
    assert.equal(await ev(page, () => db.settings.youtube), '');
    await ctx.close();
  });

  test('une sortie avec un lien 68° en http est enregistrée sans le lien', async () => {
    const { page, ctx } = await open();
    await ev(page, () => { /* simule une saisie qui contourne le champ url */ });
    await asPresident(page);
    await admin(page, 'Sorties');
    const f = formWith(page, 'Titre');
    await f.getByLabel('Titre').fill('Sortie http');
    await f.getByLabel('Date').fill('2099-12-02');
    await f.getByLabel(/Lien de l’itinéraire/).evaluate(el => { el.type = 'text'; });
    await f.getByLabel(/Lien de l’itinéraire/).fill('http://mauvais.example/x');
    await f.getByRole('button', { name: 'Ajouter' }).click();
    await until(page, () => db.events.some(e => e.titre === 'Sortie http'));
    assert.equal(await ev(page, () => db.events.find(e => e.titre === 'Sortie http').lien), '');
    await ctx.close();
  });

  test('un fichier qui n’est pas une image n’est pas ajouté à l’album', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await page.goto(URL_ + '#album-a1');
    await page.locator('input[type=file]').setInputFiles({ name: 'note.txt', mimeType: 'text/plain', buffer: Buffer.from('bonjour') });
    await page.waitForTimeout(500);
    assert.equal(await ev(page, () => db.albums[0].photos.length), 0);
    await ctx.close();
  });

  test('une adresse inconnue retombe sur l’accueil, un album inexistant le dit', async () => {
    const { page, ctx } = await open('#nimporte-quoi');
    assert.equal(await page.locator('h1').first().textContent(), 'Béruges Moto Club');
    await page.goto(URL_ + '#album-introuvable');
    await page.getByText('Cet album n’existe pas.').waitFor();
    await ctx.close();
  });

  test('une recherche sans résultat l’explique', async () => {
    const { page, ctx } = await open();
    await asPresident(page);
    await page.getByLabel('Rechercher un membre').fill('zzzz');
    await page.getByText('Aucun membre ne correspond.').waitFor();
    await ctx.close();
  });

  test('« Prochaine sortie » ne montre jamais une sortie passée', async () => {
    const { page, ctx } = await open();
    await ev(page, () => { db.events.forEach(e => { e.date = '2020-01-01'; }); render(); });
    assert.equal(await ev(page, () => nextEvent()), null);
    await page.getByText('Aucune sortie prévue pour le moment').first().waitFor();
    await ctx.close();
  });

  test('l’ancien fichier partenaires.html n’existe plus', () => {
    assert.equal(fs.existsSync(path.join(SITE, 'partenaires.html')), false);
  });
});

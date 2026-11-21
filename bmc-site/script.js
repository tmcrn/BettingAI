// Menu mobile
const burger = document.querySelector('.burger');
const menu = document.getElementById('menu');
burger.addEventListener('click', () => {
  const open = menu.classList.toggle('open');
  burger.setAttribute('aria-expanded', String(open));
});
menu.addEventListener('click', (e) => {
  if (e.target.tagName === 'A') {
    menu.classList.remove('open');
    burger.setAttribute('aria-expanded', 'false');
  }
});

// Onglets de l'aperçu espace membres
const tabs = document.querySelectorAll('.tabs [role="tab"]');
tabs.forEach((tab) => {
  tab.addEventListener('click', () => {
    tabs.forEach((t) => {
      const selected = t === tab;
      t.setAttribute('aria-selected', String(selected));
      document.getElementById(t.getAttribute('aria-controls')).hidden = !selected;
    });
  });
});

// Aperçu des présences : un seul choix actif par sortie
document.querySelectorAll('.choice').forEach((group) => {
  group.addEventListener('click', (e) => {
    if (e.target.tagName !== 'BUTTON') return;
    group.querySelectorAll('button').forEach((b) => b.classList.toggle('on', b === e.target));
  });
});

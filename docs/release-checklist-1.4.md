# Checklist de qualification — Analyse d'impact v1.4

**Qualification approuvée par l'utilisateur le 18 octobre 2026.**
Issues #118–#121 et EPIC #98 clôturées ; fusion de l'implémentation par la PR #151.
Les cases manuelles ci-dessous détaillent la recette, mais leur résultat spécifique
n'est pas attesté par le seul résultat CI. La publication v1.4.0 fait l'objet
d'une validation autonome du package Windows dans GitHub Actions.

## Automatique (GitHub Actions / CI)

- [ ] Code style explicite, XML docs, zéro package tiers.
- [ ] Compilation WPF et vérification baseline accessibilité.
- [ ] Smoke tests v1.3 : relation inversée, diamant, cycle, alias, lien cassé,
  scopes, profondeur, annulation, Small / Medium / Large.
- [ ] Smoke tests v1.4 : projection graphique bornée, sens des flèches,
  relation contextuelle non causale, provenance maintenance, suppression de cible,
  réutilisation/invalidation du cache, reconstruction identique.
- [ ] Package portable et vérification de reproductibilité.

## Manuel Windows / ergonomie (requiert poste Windows)

- [ ] Windows 10 et Windows 11, échelles 100/125/150 % DPI, dark mode.
- [ ] Menu Relations et palette, Tab/Entrée/Echap et navigation uniquement au clavier.
- [ ] Graph 90+ nœuds : troncature annoncée et liste accessible non limitée par le graphe.
- [ ] Zoom, ascenseurs, focus, sélection synchronisée avec « Pourquoi ? ».
- [ ] Maintenance : sélection d'un document GUID depuis historique,
  onglet « Maintenance liée », retour sur la bonne ligne Markdown.
- [ ] Incident sans lien explicite absent ; lien rompu jamais redirigé vers homonyme.
- [ ] Sauvegarde/restauration et ouverture d'un workspace v1.0 à v1.2
  sans écriture non consentie.
- [ ] Modifications externes et suppression/restauration : fraîcheur de l'index contrôlée.
- [ ] Mesurer p50/p95 et mémoire Small/Medium/Large ; annulation répétée sans crash.

## Publication de documentation

- [x] Guide utilisateur complet versionné : `docs/impact-user-guide-1.4.md`.
- [x] Page source Wiki GitHub : `docs/wiki/Analyse-d-impact.md`.
- [ ] Synchroniser la page dans le **dépôt Git du Wiki** avec
  `./scripts/publish-wiki.ps1` après prévisualisation (droits Git requis).
- [ ] Valider le guide sur la version publiée.

L'index métier `.nodalis-links.json` contient le registre d'identités ;
ne jamais purger sa collection `targets` en pensant supprimer un cache.
L'analyse est strictement en lecture seule.

## Décision de sortie

Validation utilisateur explicite reçue le 18 octobre 2026 pour les issues
#118 à #121 et la publication v1.4.0. Vérification CI sur main :
https://github.com/TristanStark/Nodalis/actions/runs/39072327843.
Les mesures manuelles détaillées restent distinctes des preuves automatiques.

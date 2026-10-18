# Nodalis v1.6 — Qualification des documents composites (#141)

**Statut : candidate en développement sur `develop`.**
Aucune release v1.6.0, aucun tag, aucune fusion vers main avant validation utilisateur.

## Gates automatiques de la CI

- Compilation Release, typage explicite, documentation XML, tests zéro dépendance, publication portable Windows.
- `CompositeDocumentsSmokeTests` : GUID, alias ambigus, sections, code fences, cibles invalides, cycles, annulation, 120 inclusions distinctes, budget de 256 fragments.
- `CompositeExportsSmokeTests` : Markdown avec références vs instantané résolu, échappement HTML, archive OpenXML Word et absence d'écriture dans les sources.
- Graphe d'inclusions dérivé : parents transitifs, reconstruction après suppression d'un cache.
- Conservation des données 1.0–1.5 ; aucune nouvelle structure obligatoire pour un projet ultra-simple.

## Vérifications Windows manuelles

- [ ] Win10/Win11 en mode sombre, DPI 100/125/150 %, navigation clavier seule dans le sélecteur document/section.
- [ ] Insertion de `![[GUID#Titre]]`, preview lisible et source ouvrable exactement à sa section.
- [ ] Modifier la source hors Nodalis : aperçu du parent rafraîchi sans duplication.
- [ ] Renommer/déplacer/supprimer/restaurer la source : GUID préservé ou diagnostic explicite.
- [ ] Cycle A → B → A et 120 fragments : interface responsive et pas de boucle infinie.
- [ ] Export Markdown référence, Markdown résolu, HTML, DOCX ; inspection Word/LibreOffice et retour source.
- [ ] Export enrichi : images locales, tableaux, liens, Mermaid, caractères spéciaux, mise en page imprimable.
- [ ] Backup puis restauration d'un workspace 1.5 avec documents composites ; aucun changement dans les fichiers canoniques.
- [ ] Mesures Small/Medium/Large : mémoire, latence froide/chaude, annulations fréquentes.
- [ ] Toutes les issues #135–#141 validées ; aucun P0/P1 bloquant.

## Limites documentées

- Seules les inclusions placées sur une ligne autonome sont interprétées.
- Les titres identiques sont refusés pour une sélection de section (pas de section ambiguë chargée).
- Les limites visent d'abord l'absence de blocage, pas un rendu de document illimité.
- Le DOCX initial exporte les titres, les paragraphes et tableaux simples. Images, Mermaid, riches blocs imbriqués et typographie précise nécessitent encore validation et enrichissement. L'impression native dédiée n'est pas encore qualifiée.
- Le cache d'inclusion est reconstructible ; la source reste toujours Markdown.

## Procédure de retour

Revenir à la 1.5.0 publiée ne détruit pas les sources Markdown : les lignes d'inclusion demeurent lisibles, mais ne sont pas résolues dans l'ancien aperçu. Restaurer un ZIP de workspace s'il faut récupérer un ancien état de contenu, puis vérifier GUID/alias et sections.

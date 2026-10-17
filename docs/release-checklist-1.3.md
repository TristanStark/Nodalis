# Qualification Nodalis v1.3 — Analyse d'impact

Statut : **à valider avant tag v1.3.0**. La v1.2.0 est la base publiée ;
les évolutions v1.3 sont développées uniquement sur `develop`.

## Tests automatiques

- [x] Table de vérité : les sept relations natives dans les deux sens (#113).
- [x] Un type de relation inconnu reste contextuel, sans propagation.
- [x] Chaîne C → B → A, diamant, cycles : résultats déterministes et un chemin explicatif court.
- [x] Backlinks simples : suggestions non causales, jamais des dépendances explicites.
- [x] Référence cassée, renommage conservant le GUID, reconstruction du graphe.
- [x] Frontière de projet par défaut et option explicite de traversée des scopes.
- [x] Limites de profondeur, nombre de nœuds, résultat tronqué et annulation.
- [x] Fixtures de graphes Small / Medium / Large (16 / 300 / 1800 documents), mesures p50/p95 consignées dans les logs de smoke tests, sans seuil CI fragile.
- [ ] CI du dernier commit : compilation, code style, accessibilité, smoke tests, portable.
- [ ] Comparer avec une reconstruction à partir du Markdown et du registre réindexé après des changements externes sur un workspace réel.

## Validation manuelle demandée (Windows)

- [ ] Windows 10 et Windows 11 : ouvrir un workspace 1.0 puis sélectionner un document et utiliser **Impacts…** et la palette.
- [ ] Vérifier 100 %, 125 % et 150 % DPI, thème sombre, zoom texte, fenêtre étroite.
- [ ] Clavier uniquement : Tab, flèches, Entrée et Échap, navigation vers un résultat et chacune de ses preuves.
- [ ] Modifier B après création de « A dépend de B », rafraîchir, analyser B : A est potentiellement affecté ; analyser A : B ne l'est pas.
- [ ] Ajouter `[[B]]` depuis A sans relation typée : résultat suggéré uniquement.
- [ ] Supprimer/restaurer/renommer B et constater des diagnostics appropriés sans ID fictif.
- [ ] Utiliser les filtres preuve, niveau, profondeur, périmètre transversal ; vérifier le message en cas de troncature.
- [ ] Annuler pendant l'analyse d'un gros workspace : aucune fermeture brutale et aucun changement dans les fichiers.
- [ ] Contrôler les performances sur un workspace réel puis documenter les mesures.

Aucune release n'est publiée automatiquement à partir de cette checklist. Le tag
`v1.3.0` est réservé à la fin de la qualification fonctionnelle et visuelle.

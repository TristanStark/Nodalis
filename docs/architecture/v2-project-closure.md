# Nodalis 2.0 — Pré-audit de clôture (#204)

## Premier incrément sur `develop`

Avant l'archivage via la commande existante « Clore et archiver », Nodalis
propose un **pré-audit consultatif** calculé sur demande, sans initier de
migration ni créer de nouveau registre. Le travail de lecture se déroule
hors du thread WPF. L'utilisateur peut refuser ou poursuivre l'archivage
après revue des constats ; aucun élément ouvert ne devient bloquant.

Les quatre sources canoniques actuellement couvertes sont :

- **Risques** : fiches RIS dont le cycle n'est pas `Closed` ou la
  qualification n'est pas `Validated`.
- **Exigences** : fiches EXI non rejetées/obsolètes dont la validation
  explicite et la vérification ne sont pas toutes deux respectivement
  `Approved` et `Verified`.
- **MEP** : opérations sans bilan métier explicite.
- **Réserves MEP** : obligations RES toujours `Open` ou `InProgress`
  après reconstruction de l'historique append-only.

Les identités et les références affichées viennent des documents existants.
Chaque lecteur vérifie son périmètre projet ; les catalogues ne sont pas
modifiés. Un registre avec propriétaire divergent provoque une erreur,
jamais une inclusion silencieuse d'éléments externes. Les diagnostics de
lecture existants sont conservés : ils indiquent une **couverture
potentiellement incomplète**, et non un état « tout est soldé ».

Le pré-audit présente les comptages, au plus huit objets détaillés depuis
l'interface et jusqu'à quatre diagnostics. Les autres éléments sont
comptabilisés, sans générer une boîte de dialogue démesurée. Cette revue
**ne constitue pas un bilan de clôture signé**.

## Limites explicites du premier incrément

Le décompte n'inclut pas encore les tâches Markdown, jalons, incidents,
campagnes de tests, points de contrôle ni les transferts vers un projet
successeur. L'utilisateur doit les examiner indépendamment : un total
« 0 élément » n'atteste pas que le projet est intégralement soldé.

Il n'y a pas encore de justification persistée, de versionnement du bilan,
de mouvements d'objets ou de journal de transfert dans ce lot. Le service
d'archivage 1.9 existant reste responsable du déplacement réversible du
dossier complet, de la prévention de collisions et des imports DOCX en
attente. Aucune écriture dans les documents métier n'est déclenchée
par la préparation de la revue.

## Régressions

`ProjectClosureAuditSmokeTests` vérifie les états humains ouverts puis
résolus, les GUIDs stables, le refus d'un autre propriétaire, la non-
mutation des registres en mémoire et la lecture d'un ancien projet
Ultra-simple sans création de fichiers v2. La qualification CI et la
qualification visuelle Windows restent nécessaires.

## Suites de #204

1. Inventaire exhaustif à l'échelle du projet, sans rescans globaux
   (tâches, jalons, incidents, campagnes, contrôles).
2. Bilan versionné `Management/Bilan de clôture.md` avec auteur,
   justification, preuves, dettes et décisions humaines.
3. Passation au cas par cas : déplacement à identité conservée ou
   successeur à nouvelle identité, chacun avec prévisualisation,
   sauvegarde, reprise et journal de provenance.
4. Validation du scénario de clôture puis réactivation des archives,
   prévention des liens cassés et qualification visuelle.

Aucun merge `main`, tag ni release ne découle de ce lot.

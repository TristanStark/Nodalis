# Tests et campagnes de recette — Nodalis 2.0

## Référentiel de tests identifiable (incrément 1)

Les tests sont des **blocs Markdown à GUID stable** dans de grands cahiers
ou des fiches individuelles. Un titre explicite `## TEST-0001 — Libellé`
ou `## TST-001 — Libellé` est suivi du marqueur déjà employé par Nodalis :
`<!-- nodalis:block:<GUID> -->`. Les titres de niveaux 1 à 6 sont pris
en charge ; les titres et marqueurs placés dans des blocs de code sont
ignorés. Les titres/numéros ne remplacent jamais l'identité GUID.

Le codec `ProjectTestDefinitionCodec.Inspect` est en lecture seule :
il construit une section **exacte** du titre du test jusqu'au prochain
titre de niveau égal ou supérieur, y compris ses sous-titres Étapes et
Attendu, avec son empreinte SHA-256. L'édition d'un autre test dans le
même grand document ne change pas cet instantané. Les doublons de GUID,
références répétées, marqueurs manquants ou malformés sont diagnostiqués
et ne deviennent pas des tests « gelables ».

Les smoke tests créent un cahier entièrement synthétique de **180 tests**,
vérifient les frontières des sections, les renommages, les duplications,
les empreintes indépendantes et l'absence de mutation du Markdown.

Cet incrément **n'exécute aucun test**, ne crée aucune campagne et
ne valide aucun critère/exigence. Les passages de campagne, résultats
et preuves sont à implémenter séparément dans l'EPIC #200.

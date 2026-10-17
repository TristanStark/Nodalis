# Nodalis 1.3 — sémantique des impacts (#113–#115)

Une analyse répond à **« Si cet élément change, quels autres éléments pourraient
être affectés, et pourquoi ? »**, jamais à « Quels éléments seront modifiés ? ».

## Table de vérité (relation telle qu'écrite : A → B)

| Relation dans A | Changement de B → A | Changement de A → B | Preuve |
| --- | --- | --- | --- |
| A **dépend de** B | Potentiel, explicite | Non | Relation typée |
| A **implémente** B | Potentiel, explicite | Non | Relation typée |
| A **teste** B | Potentiel, explicite | Non | Relation typée |
| A **documente** B | Potentiel, explicite | Non | Relation typée |
| A **bloque** B | Contexte seulement, non transitif | Contexte seulement, non transitif | Suggestion |
| A **remplace** B | Contexte seulement, non transitif | Contexte seulement, non transitif | Suggestion |
| A **est lié à** B | Contexte seulement, non transitif | Contexte seulement, non transitif | Suggestion |
| A **type inconnu** B | Contexte seulement, non transitif | Contexte seulement, non transitif | Suggestion |
| A contient un lien **[[B]]** | Contexte seulement, non transitif | Non | Backlink simple |

La relation `bloque` n'est pas, à elle seule, la preuve d'une dépendance
technique : elle peut représenter un état de workflow. `remplace` est historique,
pas une garantie de compatibilité descendante. Pour les quatre relations qui
propagent, la nature « explicite » s'applique **à la relation**, pas à la
certitude d'un impact réel. Le vocabulaire de l'interface doit conserver
« potentiellement affecté ».

## Calcul

Le moteur reconstitue en mémoire une projection de `.nodalis-links.json` :
`targets` conserve les identités canoniques, tandis que `references` et
`relations` sont des projections du Markdown canonique. Le moteur **n'écrit
aucune donnée** et ne persiste aucun second registre d'identité. Il doit
recevoir un index de liens rafraîchi après les modifications externes.

- Chaque nœud est identifié par le GUID stable de la cible active.
- Chaque arête résolue contient : source, cible, type, emplacement de provenance
  (document + ligne), extrait, sens et niveau de preuve.
- Une cible introuvable, invalide ou un lien ambigu donne un diagnostic, jamais
  une arête vers un objet fictif. Suppression/restauration et renommage utilisent
  les identités déjà gérées par le registre de liens 1.0.
- Le parcours en largeur suit uniquement le sens **cible → source** d'une
  relation propagatrice. Il retient le plus court chemin explicatif par nœud,
  dédoublonné et déterministe ; les cycles ne sont jamais parcourus en boucle.
- Les suggestions contextuelles sont limitées à **un saut** depuis la source
  analysée et ne déclenchent aucune propagation. Elles restent étiquetées
  « suggéré » même lorsqu'une relation typée les porte.
- La profondeur, le nombre de résultats et le nombre de nœuds visités sont
  bornés ; un résultat incomplet signale explicitement sa troncature.
  L'annulation interrompt le calcul sans aucune écriture.
- Par défaut, seuls les éléments du même `ScopeIdentity` sont inclus :
  projet, module, application ou global. La recherche transversale est une
  option volontaire (`IncludeCrossScope`).
- Les décisions/tâches/passages Markdown sans identité stable persistée
  ne deviennent **pas** artificiellement des nœuds. Les documents des décisions
  restent analysables en tant que documents ; l'analyse plus fine de blocs
  demande un contrat explicite ultérieur.

## Compatibilité et reconstruction

Pas de changement du schéma workspace 1 ni de `.nodalis-links.json` v3.
Ne pas effacer la collection `targets` de ce fichier : elle constitue le
registre d'identité technique et ses alias de renommage.

`ImpactGraphIndex.Build` reconstruit toute sa projection en mémoire, avec les
mêmes arêtes pour les mêmes entrées ; les IDs d'occurrence sont dérivés et
ne doivent pas être utilisés comme identités métier durables.

Le catalogue de règles (`ImpactRelationPolicy`) est la source de vérité
logicielle des directions documentées ci-dessus. Toute évolution de cette
table devra s'accompagner de tests pour les deux sens de chaque relation.

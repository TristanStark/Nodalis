# Nodalis — Guide de l'analyse d'impact (1.3 / 1.4)

> **Disponibilité au 17 octobre 2026 :** la dernière release publique est
> **v1.2.0**. Les fonctions d'analyse d'impact ci-dessous sont intégrées dans
> la branche `develop`, **pas encore dans une release stable v1.4**.
> Elles fonctionnent entièrement hors ligne et sans activer l'IA.

## À quoi sert l'analyse d'impact ?

Pour une modification envisagée d'une spécification, d'une note technique,
d'un test ou d'une décision documentée, Nodalis peut répondre :

**« Quels autres documents pourraient être affectés ? Quelles relations
justifient ce résultat ? »**

Il affiche des **impacts potentiels**, jamais des garanties de panne ni des
modifications automatiques.

### Exemple pratique

Créez trois documents : `API.md`, `Service.md` et `Validation.md`.

1. Dans `Service.md`, cliquez **+ Ajouter** (section **Relations**), choisissez
   `dépend de`, puis ciblez `API.md`.
2. Dans `Validation.md`, ajoutez la relation `teste` vers `Service.md`.
3. Sélectionnez `API.md` dans l'explorateur et cliquez **Impacts…**.
4. La vue **Liste accessible** affiche `Service.md` au niveau 1 et
   `Validation.md` au niveau 2, avec leurs chemins justificatifs.
5. Sélectionnez `Validation.md`, lisez **Pourquoi cet impact ?** puis
   sélectionnez une étape et cliquez **Ouvrir l'étape sélectionnée**.
6. La vue **Graphe visuel** représente les mêmes relations avec des flèches
   partant de l'élément modifié vers les éléments potentiellement affectés.

Si vous analysez `Validation.md`, Nodalis ne conclut pas pour autant que
`API.md` changera : les relations ont un **sens**.

## Accéder à l'analyse

- **Panneau droit → Relations → Impacts…** avec un document Markdown sélectionné.
- **Ctrl+P → « Analyser les impacts »** dans la palette de commandes.
- **Historique de maintenance → sélectionner un incident ou une intervention
  → Analyser les impacts** : Nodalis demande de choisir un document lié
  explicitement, puis ouvre l'analyse sur ce document précis.

Il faut d'abord **créer des relations typées** pour obtenir des dépendances
significatives. Les liens `[[Nom de document]]` servent uniquement de contexte.

## Comprendre les trois onglets

### Liste accessible

Chaque ligne montre le document concerné, le type de preuve, le niveau
de propagation, la portée et les relations utilisées. Le panneau **Pourquoi
cet impact ?** montre les étapes de la chaîne, les types de relation et
les **lignes source**. Double-clic, bouton **Ouvrir le résultat**, ou
Entrée depuis la liste pour ouvrir la cible. Les étapes sont également
navigables au clavier et s'ouvrent sur la ligne Markdown d'origine.

Les résultats explicitement dérivés de `dépend de`, `implémente`, `teste`
ou `documente` sont marqués **relation explicite** ; cela prouve **le lien**,
pas un impact réel certain.

### Graphe visuel (1.4)

- Nœud de départ : le document sélectionné.
- Une flèche relie le document modifié au document potentiellement affecté.
- Traits continus : dépendance typée explicite. Traits pointillés :
  suggestion de contexte, **non propagée**.
- Cliquez sur un nœud : il devient la sélection active de la liste et son
  chemin apparaît dans **Pourquoi cet impact ?**.
- Le curseur de zoom varie de 40 à 180 %. **Recentrer** réinitialise le zoom
  et le défilement ; les ascenseurs permettent le déplacement/panoramique.
- Pour un grand graphe, seules **90 entités maximum** sont dessinées :
  utilisez la liste pour consulter les autres résultats. Une
  limitation d'affichage n'élimine aucun résultat du calcul déjà effectué.

### Maintenance liée (1.4)

Cet onglet présente les **incidents et interventions** dont le bloc Markdown
contient une relation typée vers l'un des documents de l'analyse affichée.
Chaque résultat conserve le type de lien, le nom du document, l'ID du bloc
de maintenance, sa provenance et son emplacement. Sélectionnez la ligne, puis
**Ouvrir la fiche à la bonne ligne** pour retrouver la source.

**Important :** « cette intervention documente l'API » ne signifie pas
« cette intervention sera nécessaire si l'API change ». Les références
maintenance enrichissent le **contexte** ; elles ne créent pas de dépendances
causales. Un incident sans relation explicite ne sera pas associé arbitrairement
à un document au seul motif qu'ils partagent des mots.

## Filtres et périmètre

La zone de recherche filtre par document ou relation, la liste **Preuves**
sépare liens explicites et suggestions, **Niveau** cible Projet /
Application / Module / Global ; la profondeur est configurable
à 1, 2, 3, 4, 6 ou 8 sauts.

**Par défaut, l'analyse ne sort pas du périmètre stable du document.**
Cocher **Inclure les autres projets, applications et le global** pour parcourir
les relations transversales. Les suggestions contextuelles peuvent être masquées
sans perte de liens. La liste, le graphe et le panneau maintenance se fondent
sur les mêmes résultats filtrés.

### Relations et sens de propagation

| Relation écrite dans A vers B | Si B change | Si A change |
| --- | --- | --- |
| `dépend de` | A est potentiellement affecté | Pas de propagation vers B |
| `implémente` | A est potentiellement affecté | Pas de propagation vers B |
| `teste` | A pourrait nécessiter une révision | Pas de propagation vers B |
| `documente` | A pourrait nécessiter une mise à jour | Pas de propagation vers B |
| `bloque`, `remplace`, `est lié à` | Simple suggestion de contexte | Simple suggestion de contexte |
| Type de relation personnalisé | Simple suggestion de contexte | Simple suggestion de contexte |
| Simple lien Markdown de A vers B | A peut apparaître comme contexte de B | Pas de propagation vers B |

Une suggestion n'est **jamais propagée indirectement**, même si elle est
présente au milieu d'un réseau de relations.

## Limites, sécurité et performances

- Les éléments affichés ont une **identité GUID existante**, pas un GUID
  inventé par l'analyse. Les tâches ou paragraphes sans ID persistant ne
  deviennent pas artificiellement des nœuds.
- Les relations orphelines, les IDs invalides et les liens ambigus sont signalés
  et ignorés ; ils ne sont jamais redirigés vers un document homonyme.
- Les cycles sont bloqués par un ensemble des nœuds déjà visités.
- Limites actuelles du calcul : **250 résultats**, **1 500 nœuds visités**,
  profondeur ajustable. Toute analyse tronquée le signale explicitement.
- **Annuler le calcul** stoppe le parcours. Les changements de périmètre
  relancent un calcul asynchrone, sans écriture métier.
- Un cache de graphe **en mémoire**, validé par empreinte du catalogue de
  liens, évite une reconstruction identique à chaque ouverture. Le registre
  `.nodalis-links.json` reste la référence des identités ; le cache du
  graphe ne crée aucun nouveau fichier obligatoire.
- **Aucune IA, aucun cloud et aucun package tiers** ne sont nécessaires.
- Les documents ne sont jamais modifiés par une analyse. Pour les modifications
  externes, faites actualiser l'index de liens Nodalis avant de réexécuter
  l'analyse ; la vue n'effectue pas de synchronisation forcée silencieuse.

## Dépannage

**Je ne vois aucun impact :** vérifier que les documents possèdent des
relations typées avec la bonne direction, que la cible existe encore et que
l'option interprojets est activée si nécessaire.

**Je vois une « suggestion » :** un simple `[[lien]]`, `bloque`,
`remplace` ou `est lié à` ne justifie pas une dépendance métier.
Créez la relation structurée appropriée après vérification.

**Le graphe ne montre pas toute la liste :** le rendu est borné pour éviter
le gel WPF ; la liste reste l'outil de référence et le message de troncature
indique si le calcul lui-même a également été limité.

**Une intervention n'apparaît pas :** assurez-vous qu'elle possède un
identifiant stable et une relation typée enregistrée dans son bloc de
la fiche de maintenance. Les interventions non structurées ou les
cibles introuvables restent hors analyse.

**Je veux un format technique détaillé :** voir
[les règles de propagation](impact-semantics-1.3.md) et
[la checklist de qualification](release-checklist-1.4.md).

## État de qualification

Le code et les tests CI sont sur `develop`. La validation manuelle finale
sur Windows 10/11, dark mode, clavier et DPI 100/125/150 % est une étape
distincte avant toute release 1.4. **Ne pas confondre « disponible sur develop »
et « publié comme version stable ».**

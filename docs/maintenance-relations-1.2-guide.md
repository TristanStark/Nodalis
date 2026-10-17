# Nodalis 1.2 — relations de maintenance (#111)

## Contrat existant

Le mécanisme reprend les relations typées des documents Nodalis 1.0,
sans créer un second registre de dépendances :

~~~markdown
## Historique de maintenance

### Incident
- **Identifiant :** INC-...
- **Date :** 2026-10-17
- **Résumé :** Incident initial
> Relation: documente | Cible: Spécification API.md | ID: 0f447774-9465-475a-91a2-f7b0f665c9bc

### Intervention
> Relation: est lié à | Cible: Décision technique.md | ID: e8866756-e53d-486f-9b73-f3bc8ac90127
- **Identifiant :** INT-...
- **Date :** 2026-10-17
- **Résumé :** Correction réalisée
- **Incident :** INC-...
~~~

Le GUID est l'identifiant stable dans le registre d'identité 1.0.
Le libellé est seulement informatif et ne sert jamais à retrouver la cible.
Les types sont ceux de RelationTypeCatalog avec types personnalisés possibles.
Le type proposé par défaut « est lié à » reste neutre : aucune dépendance
technique n'est supposée.

## Utilisation

Dans **Historique de maintenance**, sélectionner un incident ou une
intervention puis **Ajouter une relation**. Le sélecteur des relations
standard permet de choisir le type et un document du workspace.
La relation est ajoutée à ce bloc précis, sans modifier les autres notes.
Un doublon de type et GUID n'est pas réinséré.

Cliquer sur **Références** pour voir les cibles existantes ou cassées
et **Ouvrir le document** pour naviguer à une cible valide.

Pour un incident, la liste rassemble ses relations directes et celles
des interventions possédant explicitement son IncidentId **dans cette
même fiche**. Une relation d'un autre incident ou d'une intervention
autonome n'est jamais ajoutée implicitement.

## Documents renommés, déplacés, supprimés, restaurés

Avant ouverture, Nodalis réactualise le registre d'identité et recherche
le GUID. Un document renommé ou déplacé sous le même GUID s'ouvre
au nouveau chemin. Un document supprimé apparaît comme **référence
cassée**. Le retour d'un document avec le même GUID après restauration
rétablit la navigation sans modifier les relations stockées. Un
homonyme doté d'un GUID différent ne répare **pas** la relation.

L'ajout refuse les blocs obsolètes (empreinte SHA-256), les modifications
non enregistrées dans l'éditeur et les conflits de sauvegarde. La cible
est vérifiée dans le registre après fermeture du sélecteur. Les
écritures passent par TextDocumentSession et son écriture atomique.

## API Core pour l'analyse d'impact (#98)

MaintenanceRelationIndex est une projection **en lecture seule**, produite
à partir du Markdown et du registre courant :

- Parse(sourcePath, markdown, catalog) : enregistrements de relations
  avec RecordId (INC/INT), SourceKind, IncidentId explicite, RelationType,
  TargetId (Guid?), RawTargetId, TargetLabel, TargetRelativePath,
  LineNumber et IsBroken.
- ForIncident(incident, relations) : liens directement présents dans
  l'incident et ses interventions explicitement associées.
- ForRecord(record, relations) : liens propres à un seul bloc.

Le futur moteur peut exploiter ces références et leur type, sans inventer
de lien non déclaré, ni confondre « est lié à » et « dépend de ».
L'index de liens enregistré reste un cache reconstructible et non
une seconde source de vérité.

## Régressions et recette

MaintenanceRelationsSmokeTests vérifie les GUID, les doublons,
les révisions obsolètes, les interventions multiples, les exclusions
d'associations implicites, le changement de chemin à identité constante,
les suppressions/restaurations, le Markdown libre et les conflits externes.

La vérification interactive sur Windows (DPI, dark mode, focus,
navigation au clavier et opérations réelles de restauration) reste à faire.

Voir également [Historique de maintenance](maintenance-history-1.2-guide.md)
et [Conversion Inbox](maintenance-inbox-1.2-guide.md).

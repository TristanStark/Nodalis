# Nodalis 2.0 — import de réponses IA externes (premier contrat)

Issue #197, dépendances #193 et #196. **Ce contrat ne modifie aucun projet**.
`nodalis.import.v1` est une enveloppe JSON de PROPOSITIONS et non un
protocole d'ordres exécutables.

## Format minimal

```json
{
  "schema": "nodalis.import.v1",
  "responseId": "aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa",
  "promptKey": "frt100-01",
  "proposals": [
    {
      "proposalId": "bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb",
      "kind": "requirement",
      "operation": "create",
      "certainty": "explicit",
      "sourcePath": "Applications/Exemple/Projets/Fictif/Notes.md",
      "proposedFields": {
        "title": "Exemple fictif",
        "description": "Une exigence à contrôler manuellement."
      }
    }
  ]
}
```

Les clés de propriétés sont en camelCase, les valeurs des champs proposés
sont des **chaînes**. `kind` : requirement, criterion, risk, test, task,
decision ; `operation` : create, update ou link ; `certainty` : explicit,
derived ou hypothesis. Pour une mise à jour, `baseFields` optionnel
contient les valeurs effectivement vues à la préparation du prompt.
`targetId` et `candidateReference` ne sont que des indications à
résoudre par le moteur Core sous contrôle de l'utilisateur.

Le lecteur accepte le JSON brut ou **un unique bloc** ```json entourant
toute la réponse. Limites : 1 Mio, 100 propositions, 30 champs proposés par
objet, 8000 caractères par valeur, profondeur JSON 32. Identités vides,
doublons de clés (insensibles à la casse), traversals de chemins,
types inconnus, actions `delete` et champs d'identité/approbation/statut
sont rejetés. SHA-256 calculé sur la réponse brute, sans persistance.

`ProjectAiThreeWayDiff.Compare` classe chaque champ en candidature à
examiner, déjà présent, conflit concurrent ou base inconnue. Aucun
état n'est validé automatiquement et aucune IA ne produit de GO/NO-GO.
Il n'existe pas encore de matching sécurisé vers les objets, d'interface
d'acceptation/rejet, de journaux persistés ni de transaction de commit :
**cet incrément est un parser et une projection en lecture seule**.

Les tests synthétiques vérifient collisions, erreurs, contraintes,
idempotence des identifiants et absence d'écrasement par comparaison.
Les prochaines étapes #197 sont l'interface de prévisualisation,
les sources/versions, la qualification champ par champ et le commit
récupérable validé par l'utilisateur.

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

## Première interface WPF de prévisualisation

Dans l'onglet **Parcours**, la commande **Analyser une réponse IA…**
ouvre un dialogue de collage JSON. L'analyse du schéma
`nodalis.import.v1` et des limites se fait **hors Dispatcher WPF**.
Une liste virtualisée affiche jusqu'à 100 propositions, leur type,
leur action, leur degré de certitude déclaré et leur référence candidate.
Le détail affiche les champs et la base déclarée sans présumer du
contenu réel du projet. Une modification de la zone de texte invalide
immédiatement les résultats précédents.

Cette première interface est **strictement consultative** : elle ne
possède aucun bouton d'acceptation, aucun service d'écriture ni
connexion réseau. La prochaine tranche intégrera une résolution
d'identité projet explicite et un diff à trois voies depuis une
source canonique revalidée avant commit.


## Deuxième incrément — comparaison au registre canonique (03/11/2026)

Dans **Parcours → Analyser une réponse IA…**, le bouton
**Comparer aux exigences du projet** apparaît après une analyse JSON réussie.
Ce bouton est distinct de l'analyse : il lit exclusivement le registre
canonique `Documentation/Exigences` du **projet sélectionné** via
`ProjectRequirementRegistryService`. La lecture et la résolution s'exécutent
hors Dispatcher WPF ; aucune écriture ni recherche globale n'a lieu.

Pour les propositions `requirement/update`, la résolution suit une
priorité stricte :

1. `targetId` : GUID présent et unique dans le **même projet**.
   Un GUID absent ne déclenche aucun repli automatique.
2. En l'absence de GUID : `candidateReference` doit être une référence
   canonique exacte `EXI-xxxx`. Le résultat reste indicatif.
3. Le titre et `sourcePath` ne peuvent **jamais** déterminer la cible.
   `sourcePath` est une indication de provenance non fiable : elle ne
   donne pas le droit de lire un document d'une autre application/projet.
4. Référence et GUID contradictoires, doublons ou cible absente :
   correspondance bloquée ; la réponse IA ne peut corriger ces incohérences.

La proposition `create` n'est qu'un brouillon. Une référence déjà occupée
ou un `targetId` injecté par l'IA produisent un avertissement de collision.
Les types `risk/test/task/criterion/decision` et l'opération `link` sont
pour l'instant signalés **non résolus**, plutôt que devinés.

Le premier vrai diff à trois voies couvre uniquement le champ canonique
`title` d'une fiche EXI v2 ; les autres champs restent *non vérifiés*
jusqu'à l'ajout de leurs lecteurs métier. Les valeurs de base sont celles
**déclarées par l'IA** et ne sont pas authentifiées : une égalité ne constitue
jamais un consentement à l'écriture. La prévisualisation affiche l'ID
réel, la référence, le chemin et l'empreinte SHA-256 instantanée.

**Limites :** pas encore de création, modification, validation par champ,
journal de provenance ou commit transactionnel. Un fichier peut changer
après la lecture : une future mutation nécessitera une relecture des octets
et un contrôle optimiste d'empreinte immédiatement avant toute écriture.

### Recette Windows (à qualifier)

- Coller une proposition `requirement/update` avec GUID d'une exigence du
  projet : vérifier la référence, la valeur actuelle et le diff du titre.
- Tester une référence EXI exacte sans GUID, puis un GUID d'un **autre
  projet** accompagné d'une référence valide du projet actuel : le second
  cas doit rester **introuvable**, sans repli dangereux.
- Modifier localement le titre entre la préparation du prompt et la
  comparaison : voir `ConcurrentConflict`. Sans base, voir `UnknownBase`.
- Soumettre des GUID/références contradictoires, une création avec référence
  déjà occupée, une proposition de risque et un champ `description` :
  aucune ne doit recevoir une écriture ni un diff inventé.
- Recommencer après une modification du JSON : les résultats anciens sont
  invalidés. Vérifier petit écran, clavier, grands registres, retour de
  veille et absence de gel.

Les régressions automatisées portent le nom
`ProjectAiRequirementReviewSmokeTests`. Aucun merge vers `main`,
tag ou release 2.0 ne découle de cette étape.

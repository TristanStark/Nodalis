# Nodalis v1.9 — fiche de qualification manuelle DOCX

> **Statut : branche `develop`, prépublication.** Cette grille couvre le parcours
> d'import DOCX et les réimports en cours de développement. Elle ne remplace pas
> la CI et n'implique pas que l'EPIC #154 soit terminée.

## Préparation

1. Utiliser la dernière compilation **Build** de la branche `develop` sur un
   **workspace de test ou la démo**, pas sur le workspace professionnel de production.
2. Sauvegarder le workspace avant chaque scénario d'échec. Conserver le Word
   original en dehors du workspace.
3. Préparer un Word contenant `Développement`, `Tests`, `Recette` comme titres
   Word (styles Titre 1/2), avec quelques paragraphes et un tableau de tests.
4. Ajouter une image intégrée, un lien, une sous-section et une section
   `À ignorer`. Préparer un second DOCX avec une section modifiée pour le réimport.
5. Noter pour chaque scénario : **OK / KO / Non testé**, version/commit et capture
   si KO (en masquant le contenu sensible).

## Parcours prioritaires pour validation utilisateur

| ID | Manipulation | Résultat attendu |
|---|---|---|
| Q01 | Importer le Word dans un **nouveau projet**, mapper une section vers `Section DOCX personnalisée`. | Projet créé, section personnalisée et fichier Markdown lisibles. |
| Q02 | Mapper Développement → Technique, Tests → Tests, Recette → Recette. | Chaque contenu choisi est dans la cible annoncée par le plan. |
| Q03 | Exclure `À ignorer` et une sous-section seulement. | Aucune partie exclue n'apparaît dans le Markdown ni comme média copié. |
| Q04 | Faire une sélection fine (paragraphes, tableau, image), puis vérifier l'aperçu. | Ordre du contenu conservé ; problèmes de fidélité signalés. |
| Q05 | Réimporter exactement le même Word après modification locale du Markdown. | Aucun effacement silencieux ; arbitrage explicite des conflits si proposé. |
| Q06 | Modifier Word **et** la cible Nodalis avant réimport. | Conflit conservé jusqu'à décision ; les ajouts manuels ne disparaissent pas. |
| Q07 | Réimporter une version renommée/sélectionner explicitement l'historique. | Provenance et correspondances visibles, pas d'association arbitraire. |
| Q08 | Annuler avant validation, puis inspecter projet et source. | Source inchangée ; aucun nouveau Markdown validé. |
| Q09 | Réimporter un DOCX incomplet/corrompu (copie de test). | Erreur compréhensible ; fichiers précédents préservés. |
| Q10 | Importer un document comprenant plusieurs sections vers une cible commune. | Ordre annoncé dans la fusion conservé ; pas de doublons inattendus. |
| Q11 | Dans le même Word, tester tableau fusionné et image liée distante. | Diagnostic explicite ; aucune ressource distante téléchargée. |
| Q12 | Après un échec, inspecter `Imports/Sources`, `.nodalis-imports` et les Markdown du projet. | Pas de remplacement silencieux de l'original ; signaler tout artefact orphelin. |

### Points de vigilance

- **#165** : le parseur ZIP/XML est durci et les créations Markdown sont
  exclusives, mais la récupération après **arrêt brutal de Nodalis** lors d'un
  premier import complet doit encore être qualifiée. Ne pas provoquer
  volontairement un arrêt brutal sur des données importantes.
- **#164** : ne pas considérer un réimport comme entièrement validé sans tester
  séparément conflit Word/Nodalis, suppression Word, renommage et historique.
- **#168** : l'import de plusieurs projets depuis le même Word reste une issue
  distincte ; ne pas considérer Q02 comme une validation de ce parcours.
- **#172/#173** : l'import XLSX/VSDX n'est pas disponible en v1.9.

## Signaler un défaut

Dans une issue GitHub, indiquer : **scénario**, commit `develop`,
**étapes de reproduction**, résultat attendu/observé, captures anonymisées,
et éventuelle exception/log. Ne pas joindre de vrai document professionnel
ni de workspace contenant des données confidentielles.

Suivi : [EPIC DOCX #154](https://github.com/TristanStark/Nodalis/issues/154),
[sécurité #165](https://github.com/TristanStark/Nodalis/issues/165),
[qualification de sortie #171](https://github.com/TristanStark/Nodalis/issues/171).

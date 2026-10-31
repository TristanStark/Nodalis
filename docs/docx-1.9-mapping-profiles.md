# Nodalis 1.9 — Profils DOCX dans l'assistant (#159)

## Utilisation

Dans l'onglet **Profils DOCX**, créer un profil, renseigner les conditions (titre exact normalisé, style Word, niveau, chemin parent, expression régulière limitée), choisir l'action **Map / Ignore / NewSection**, puis ajouter la règle. Le profil est éditable : ajouter/modifier, activer/désactiver, supprimer, dupliquer, sauvegarder dans `Templates/docx-mapping-profiles.json`, importer/exporter un JSON portable.

Le bouton **Prévisualiser les règles** liste les résultats pour chacune des sections, ainsi que la règle prioritaire et ses raisons. **Appliquer les propositions** ne fait que modifier l'aperçu d'import existant, jamais le DOCX source ni les fichiers Nodalis. L'onglet Fichiers conserve la validation obligatoire du plan d'écriture. Les règles contradictoires bloquent le plan tant qu'une destination n'est pas corrigée manuellement ou que l'utilisateur désactive l'usage du profil.

Les changements manuels par glisser-déposer, saisie d'une cible ou exclusion sont prioritaires et ne sont pas écrasés par une application ultérieure. **Mémoriser mes affectations** sauvegarde explicitement les choix dans le profil sous forme d'overrides titre+chemin parent pour un DOCX futur de structure similaire. Aucun profil n'est exécuté au chargement. Les profils absents ne provoquent pas de fichiers nouveaux.

Les champs de mapping sont limités, les destinations dangereuses rejetées, les regex ont un timeout et les sorties sont explicables. Le moteur n'utilise ni IA, ni Office, ni réseau. La recette visuelle Windows, la fiabilité des styles exotiques, le comportement sur DOCX de plusieurs centaines de pages et les collisions complexes restent à qualifier.

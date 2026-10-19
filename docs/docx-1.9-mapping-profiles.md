# Nodalis 1.9 — Profils DOCX déterministes (#159, socle)

Le fichier workspace `Templates/docx-mapping-profiles.json` contient un catalogue versionné de profils nommés, des règles ordonnées et des arbitrages manuels explicites. L'absence de fichier **ne provoque aucune écriture** lors de la lecture.

## Format et priorités

Un profil peut sélectionner sur **titre normalisé**, **identifiant de style Word**, **niveau de titre**, **chemin parent**, ou **motif regex sur le titre**. Toutes les conditions d'une règle sont combinées par ET ; les règles désactivées sont ignorées. L'ordre sert à nommer la règle prioritaire et à rendre les résultats reproductibles, **mais aucune priorité ne masque deux destinations contradictoires**. Dans ce cas, aucune action n'est proposée et l'utilisateur doit trancher. Les règles peuvent *Mapper*, *Ignorer* ou *Proposer une nouvelle section*. Le moteur ne crée jamais de document et ne fait aucun accès réseau ni IA.

Un override manuel sauvegardé (titre et contexte parent) a priorité sur les règles. Il est appliqué lors de l'ouverture d'un nouveau DOCX de structure similaire. Les collisions d'identité sont rejetées.

## Sécurité

Schéma 1, au plus 50 profils, 150 règles et 500 overrides par profil, champs bornés, regex limitée à 70 ms et à 4096 caractères de titre. Les chemins absolus, traversées et fichiers inattendus sont interdits pour les noms de section. Ecriture atomique JSON par `AtomicJsonFile` ; le profil d'origine est intact après validation échouée. Import/export JSON porte un seul profil, sans source Word.

## Qualification automatisée

`DocxMappingProfilesSmokeTests` couvre les titres français accentués, règles contradictoires, arbitrages sauvegardés, règles désactivées, chemins dangereux, catalogue absent, persistance atomique et export/import. Les commandes d'édition/activation dans l'interface DOCX sont intégrées séparément avant clôture de #159 ; aucune release n'est autorisée à ce stade.
